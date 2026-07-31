open System
open System.IO
open System.Net.Http
open System.Net.Security
open System.Xml.Linq

let trustedSha1s = []
    // "39BBB40368274914AA381A6BF082002D250080E1" // Ireland expired 02.07.2026

let handler = new HttpClientHandler()
handler.ServerCertificateCustomValidationCallback <-
    fun _ certificate _ sslPolicyErrors ->
        if trustedSha1s |> List.exists (fun sha1 -> certificate.Thumbprint = sha1) then true else
        sslPolicyErrors = SslPolicyErrors.None

let httpClient = new HttpClient(handler)

let rec withRetry numTry (f: unit -> 'T) =
    try
        f()
    with e ->
        if numTry < 0 then
            reraise()
        else
            withRetry (numTry - 1) f

let [<Literal>] MaxRetries = 3

let targetDir = Environment.GetEnvironmentVariable "TRUSTLIST_DOWNLOAD_DIR"
if String.IsNullOrWhiteSpace targetDir then failwith "Trustlist download directory is not defined"

let downloadFile (uri: string) fileName =
    task {
        let filePath = Path.Combine(targetDir, fileName)
        printfn "Downloading file: %s ==> %s" uri filePath
        let! fileBytes = httpClient.GetByteArrayAsync uri
        do! File.WriteAllBytesAsync(filePath, fileBytes)
        return filePath
    } |> Async.AwaitTask |> Async.RunSynchronously

let downloadFileWithRetry a b =
    withRetry MaxRetries (fun () -> downloadFile a b)

let rootDocPath = downloadFileWithRetry "https://ec.europa.eu/tools/lotl/eu-lotl.xml" "eu-lotl.xml"
let rootDoc = XDocument.Load rootDocPath

rootDoc.Elements(XName.Get("TrustServiceStatusList", "http://uri.etsi.org/02231/v2#"))
|> Seq.map (fun el -> el.Elements(XName.Get("SchemeInformation", "http://uri.etsi.org/02231/v2#")))
|> Seq.concat
|> Seq.map (fun el -> el.Elements(XName.Get("SchemeInformationURI", "http://uri.etsi.org/02231/v2#")))
|> Seq.concat
|> Seq.map (fun el -> el.Elements(XName.Get("URI", "http://uri.etsi.org/02231/v2#")))
|> Seq.concat
|> Seq.map (fun el -> el.Value)
|> Seq.filter _.EndsWith(".xml")
|> Seq.iter (fun uri -> downloadFileWithRetry uri (uri.Substring(uri.LastIndexOf '/' + 1)) |> ignore)

rootDoc.Elements(XName.Get("TrustServiceStatusList", "http://uri.etsi.org/02231/v2#"))
|> Seq.map (fun el -> el.Elements(XName.Get("SchemeInformation", "http://uri.etsi.org/02231/v2#")))
|> Seq.concat
|> Seq.map (fun el -> el.Elements(XName.Get("PointersToOtherTSL", "http://uri.etsi.org/02231/v2#")))
|> Seq.concat
|> Seq.map (fun el -> el.Elements(XName.Get("OtherTSLPointer", "http://uri.etsi.org/02231/v2#")))
|> Seq.concat
|> Seq.iter (fun el ->
    let tslLocation =
        el.Elements(XName.Get("TSLLocation", "http://uri.etsi.org/02231/v2#"))
        |> Seq.map _.Value
        |> Seq.exactlyOne
    let otherInformation =
        el.Elements(XName.Get("AdditionalInformation", "http://uri.etsi.org/02231/v2#"))
        |> Seq.map (fun el -> el.Elements(XName.Get("OtherInformation", "http://uri.etsi.org/02231/v2#")))
        |> Seq.concat
    let territory =
        otherInformation
        |> Seq.map (fun el -> el.Elements(XName.Get("SchemeTerritory", "http://uri.etsi.org/02231/v2#")))
        |> Seq.concat
        |> Seq.map _.Value
        |> Seq.exactlyOne
    let mimeType =
        otherInformation
        |> Seq.map (fun el -> el.Elements(XName.Get("MimeType", "http://uri.etsi.org/02231/v2/additionaltypes#")))
        |> Seq.concat
        |> Seq.map _.Value
        |> Seq.exactlyOne
    if mimeType = "application/vnd.etsi.tsl+xml" && territory <> "EU" then
        downloadFileWithRetry tslLocation $"%s{territory}.xml" |> ignore
)
