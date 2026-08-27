[xml]$x = Get-Content $args[0]
foreach ($p in $x.coverage.packages.package) {
    if ($p.name -match 'Application') {
        foreach ($c in $p.classes.class) {
            $lr = [double]$c.'line-rate'
            $br = [double]$c.'branch-rate'
            if ($lr -lt 1.0 -or $br -lt 1.0) {
                Write-Host "$($c.name): line=$lr branch=$br file=$($c.filename)"
            }
        }
    }
}
