<#
    medir-metodos.ps1 — mide cuantas lineas tiene cada metodo del proyecto, emparejando llaves.

    POR QUE EXISTE (03/11/2026): el diagnostico de docs/PLAN_CALIDAD_Y_OPTIMIZACION.md afirmo TRES VECES que
    habia "cinco metodos demasiado largos", con PagarAsync a la cabeza con 368 lineas. Era falso. Venia de medir
    con grep: se buscaban las firmas de metodo y se tomaba la distancia de una a la siguiente. Esa heuristica
    solo reconoce firmas de UNA linea, y los metodos largos son justo los que tienen muchos parametros y parten
    su firma en dos. Resultado: a cada metodo se le atribuian los siguientes, y el error era SISTEMATICO
    (cuanto mas larga la firma, mas se exageraba). Las cifras reales:

        PagarAsync                  368 -> 106
        CrearServicioAsync          279 -> 125
        EjecutarEnTransaccionAsync  241 -> 135
        DisponibilidadAsync         159 ->  39

    Ningun metodo del proyecto pasa de 150 lineas y la mediana es de 14. Si alguien vuelve a plantear "hay que
    partir los metodos largos", midalo con esto antes de mover codigo.

    COMO LO HACE: quita cadenas y comentarios linea por linea (una cadena puede contener // y un comentario
    puede contener llaves) y despues empareja llaves desde la firma. No hace falta conservar la longitud del
    texto borrado: solo se cuentan llaves POR LINEA, asi que las columnas no importan.
    La logica se valido contra una implementacion independiente: da los mismos numeros.

    USO (desde la carpeta del proyecto, la que tiene FlyVibeMex.csproj):

        powershell -ExecutionPolicy Bypass -File .\herramientas\medir-metodos.ps1
        powershell -ExecutionPolicy Bypass -File .\herramientas\medir-metodos.ps1 -Minimo 60

    El "-ExecutionPolicy Bypass" aplica SOLO a esa ejecucion y no cambia ninguna configuracion de la maquina.
#>

param(
    [int]$Minimo = 90,
    [string[]]$Excluir = @('\\obj\\', '\\bin\\', '\\publish\\', '_respaldo')
)

$rxCadena = @'
@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])'
'@

$archivos = Get-ChildItem -Path . -Filter *.cs -Recurse -File |
            Where-Object { $ruta = $_.FullName; -not ($Excluir | Where-Object { $ruta -match $_ }) }

$resultados = New-Object System.Collections.ArrayList
$raiz = (Get-Location).Path + '\'

foreach ($archivo in $archivos) {
    $original = @(Get-Content -LiteralPath $archivo.FullName -Encoding UTF8)
    if ($original.Count -eq 0) { continue }

    # ---- 1. Limpiar: fuera cadenas y comentarios (las cadenas PRIMERO: pueden contener //) ----
    $limpio = New-Object 'string[]' $original.Count
    $enBloque = $false
    for ($i = 0; $i -lt $original.Count; $i++) {
        $l = $original[$i]
        if ($enBloque) {
            $fin = $l.IndexOf('*/')
            if ($fin -lt 0) { $limpio[$i] = ''; continue }
            $l = $l.Substring($fin + 2); $enBloque = $false
        }
        $l = [regex]::Replace($l, $rxCadena, '')
        $corte = $l.IndexOf('//')
        if ($corte -ge 0) { $l = $l.Substring(0, $corte) }
        while ($l.IndexOf('/*') -ge 0) {
            $ini = $l.IndexOf('/*')
            $fin = $l.IndexOf('*/', $ini + 2)
            if ($fin -lt 0) { $l = $l.Substring(0, $ini); $enBloque = $true; break }
            $l = $l.Substring(0, $ini) + $l.Substring($fin + 2)
        }
        $limpio[$i] = $l
    }

    # ---- 2. Buscar firmas y emparejar llaves ----
    $i = 0
    while ($i -lt $limpio.Count) {
        $l = $limpio[$i]
        $esFirma = ($l -match '^    (public|private|protected|internal)\b') -and
                   $l.Contains('(') -and -not $l.TrimEnd().EndsWith(';') -and -not $l.Contains('=>')
        if ($esFirma) {
            # La firma puede ocupar varias lineas: acumular hasta cerrar los parentesis.
            $par = 0; $j = $i
            while ($j -lt $limpio.Count) {
                $par += ([regex]::Matches($limpio[$j], '\(')).Count - ([regex]::Matches($limpio[$j], '\)')).Count
                if ($par -le 0) { break }
                $j++
            }
            if ($j -lt $limpio.Count -and -not $limpio[$j].TrimEnd().EndsWith(';') -and -not $limpio[$j].Contains('=>')) {
                # La llave de apertura: en esa linea o en una de las dos siguientes.
                $k = -1
                $tope = [Math]::Min($j + 3, $limpio.Count)
                for ($x = $j; $x -lt $tope; $x++) {
                    if ($limpio[$x].Contains('{')) { $k = $x; break }
                }
                if ($k -ge 0) {
                    $prof = 0; $fin = -1
                    for ($x = $k; $x -lt $limpio.Count; $x++) {
                        $prof += ([regex]::Matches($limpio[$x], '\{')).Count - ([regex]::Matches($limpio[$x], '\}')).Count
                        if ($prof -eq 0) { $fin = $x; break }
                    }
                    if ($fin -ge 0) {
                        [void]$resultados.Add([pscustomobject]@{
                            Lineas  = $fin - $i + 1
                            Archivo = $archivo.FullName.Replace($raiz, '')
                            Linea   = $i + 1
                            Firma   = $original[$i].Trim()
                        })
                        $i = $fin + 1
                        continue
                    }
                }
            }
        }
        $i++
    }
}

if ($resultados.Count -eq 0) { Write-Warning "No se midio ningun metodo. Estas en la carpeta del proyecto?"; return }

$ordenados = $resultados | Sort-Object Lineas
$largos    = $resultados | Where-Object { $_.Lineas -ge $Minimo } | Sort-Object Lineas -Descending
$mediana   = $ordenados[[int]($resultados.Count / 2)].Lineas
$enormes   = @($resultados | Where-Object { $_.Lineas -gt 150 }).Count

Write-Host ""
Write-Host "Metodos de $Minimo lineas o mas:" -ForegroundColor Cyan
foreach ($m in $largos) {
    Write-Host ("  {0,4}  {1}:{2}" -f $m.Lineas, $m.Archivo, $m.Linea)
    Write-Host ("        {0}" -f $m.Firma) -ForegroundColor DarkGray
}
Write-Host ""
Write-Host ("Medidos: {0}  ·  de {1}+ lineas: {2}  ·  de 151+: {3}  ·  mediana: {4}" -f `
    $resultados.Count, $Minimo, @($largos).Count, $enormes, $mediana) -ForegroundColor Green
