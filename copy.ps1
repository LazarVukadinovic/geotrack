$Out = "..\geotrack-working-tree.zip"

git ls-files -co --exclude-standard | tar.exe -a -c -f $Out -T -

Write-Host "Created $Out"