Import-Module ActiveDirectory

Get-ADUser -Filter * -Properties DisplayName, GivenName, Surname |
    Where-Object {
        [string]::IsNullOrWhiteSpace($_.DisplayName) -or
        $_.DisplayName -match '\s{2,}'
    } |
    Select-Object Name, SamAccountName, DisplayName, GivenName, Surname |
    Sort-Object Name |
    Export-Csv -Path .\ad-displayname-audit.csv -NoTypeInformation -Encoding UTF8
