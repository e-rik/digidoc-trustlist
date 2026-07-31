#!/usr/bin/bash

set -euo pipefail

pushd $TRUSTLIST_DOWNLOAD_DIR

printf "Downloading https://ec.europa.eu/tools/lotl/eu-lotl.xml ...\n"
curl -fLO https://ec.europa.eu/tools/lotl/eu-lotl.xml --connect-timeout 10 --retry 5 --retry-delay 3 --retry-all-errors

xmllint --xpath "//*[local-name() = 'TrustServiceStatusList']/*[local-name() = 'SchemeInformation']/*[local-name() = 'SchemeInformationURI']/*[local-name() = 'URI']/text()" eu-lotl.xml \
    | grep '\.xml$' \
    |
while IFS= read -r file; do
    printf "Downloading %s ...\n" "$file"
    curl -fLO "$file" --connect-timeout 10 --retry 5 --retry-delay 3 --retry-all-errors
done

mapfile -t urls < <(xmllint --xpath "//*[local-name() = 'TrustServiceStatusList']/*[local-name() = 'SchemeInformation']/*[local-name() = 'PointersToOtherTSL']/*[local-name() = 'OtherTSLPointer']/*[local-name() = 'TSLLocation']/text()" eu-lotl.xml)
mapfile -t territories < <(xmllint --xpath "//*[local-name() = 'TrustServiceStatusList']/*[local-name() = 'SchemeInformation']/*[local-name() = 'PointersToOtherTSL']/*[local-name() = 'OtherTSLPointer']/*[local-name() = 'AdditionalInformation']/*[local-name() = 'OtherInformation']/*[local-name() = 'SchemeTerritory']/text()" eu-lotl.xml)
mapfile -t mime_types < <(xmllint --xpath "//*[local-name() = 'TrustServiceStatusList']/*[local-name() = 'SchemeInformation']/*[local-name() = 'PointersToOtherTSL']/*[local-name() = 'OtherTSLPointer']/*[local-name() = 'AdditionalInformation']/*[local-name() = 'OtherInformation']/*[local-name() = 'MimeType']/text()" eu-lotl.xml)


for i in "${!urls[@]}"; do
    if [[ "${mime_types[$i]}" == "application/vnd.etsi.tsl+xml" ]] ; then
        if [[ "${territories[$i]}" != "EU" ]]; then
            printf "Downloading %s => %s ...\n" "${urls[$i]}" "${territories[$i]}.xml"
            curl -fL "${urls[$i]}" -o "${territories[$i]}.xml" --connect-timeout 10 --retry 5 --retry-delay 3 --retry-all-errors
        fi
    fi
done

popd
