Get-ChildItem Cert:\LocalMachine\My |
    Select-Object Subject, Issuer, NotAfter, Thumbprint |
    Sort-Object NotAfter
