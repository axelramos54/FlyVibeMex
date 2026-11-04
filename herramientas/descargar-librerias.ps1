<#
    descargar-librerias.ps1 — baja a wwwroot/lib/ las librerias que el sitio usaba por CDN.

    NOTA (03/11/2026): wwwroot/lib YA ESTA VERSIONADO en Git, asi que para empezar a trabajar NO hace falta
    correr esto: al clonar el repositorio los archivos ya vienen. Este script sirve para dos cosas:
      1. Documentar exactamente que version de cada libreria usa el proyecto.
      2. Volver a bajarlas si alguna vez hay que subir de version (y entonces hay que cambiar tambien
         Views/Shared/_BootstrapCss.cshtml, _BootstrapJs.cshtml y _ValidationScriptsPartial.cshtml).

    POR QUE ESTAN ALOJADAS Y NO EN UN CDN: en una feria de proyectos, si el wifi del lugar falla, un Bootstrap
    por CDN deja el sitio SIN ESTILOS —no "mas lento": roto— y no hay forma de arreglarlo en el momento.
    Ademas se quitan dos DNS y dos TLS a dominios ajenos (lo que mas tarda en una red lenta) y dejan de hacer
    falta los hashes SRI, porque la verificacion de integridad existe para lo que sirve un tercero.

    RUTAS PLANAS A PROPOSITO: la convencion de ASP.NET es wwwroot/lib/<paquete>/dist/<archivo>, pero el
    .gitignore de este repositorio ignora `dist/` (regla de artefactos de compilacion). Con esa convencion los
    archivos NO se versionarian y no llegarian a las demas maquinas del equipo: habria fallado en silencio.

    DESPUES DE CORRER ESTO: dotnet build. MapStaticAssets() solo sirve los archivos que existian al compilar.

    USO (desde la carpeta del proyecto, la que tiene FlyVibeMex.csproj):

        powershell -ExecutionPolicy Bypass -File .\herramientas\descargar-librerias.ps1

    POR QUE ESE "-ExecutionPolicy Bypass": Windows no deja correr archivos .ps1 por omision y da el error
    "la ejecucion de scripts esta deshabilitada en este sistema" (PSSecurityException). El flag aplica SOLO a
    esta ejecucion y NO cambia ninguna configuracion de la maquina. No hace falta Set-ExecutionPolicy: eso
    bajaria de forma permanente una proteccion de Windows para correr un script de conveniencia.
#>

$ErrorActionPreference = 'Continue'

$archivos = @(
    @{ d = 'wwwroot/lib/bootstrap/bootstrap.min.css';            u = 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css' }
    @{ d = 'wwwroot/lib/bootstrap/bootstrap.min.css.map';        u = 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css.map' }
    @{ d = 'wwwroot/lib/bootstrap/bootstrap.bundle.min.js';      u = 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/js/bootstrap.bundle.min.js' }
    @{ d = 'wwwroot/lib/bootstrap/bootstrap.bundle.min.js.map';  u = 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/js/bootstrap.bundle.min.js.map' }
    @{ d = 'wwwroot/lib/bootstrap/LICENSE';                      u = 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/LICENSE' }

    @{ d = 'wwwroot/lib/jquery/jquery.min.js';                   u = 'https://cdn.jsdelivr.net/npm/jquery@3.7.1/dist/jquery.min.js' }
    @{ d = 'wwwroot/lib/jquery/LICENSE.txt';                     u = 'https://cdn.jsdelivr.net/npm/jquery@3.7.1/LICENSE.txt' }

    @{ d = 'wwwroot/lib/jquery-validation/jquery.validate.min.js'; u = 'https://cdn.jsdelivr.net/npm/jquery-validation@1.21.0/dist/jquery.validate.min.js' }
    @{ d = 'wwwroot/lib/jquery-validation/LICENSE.md';             u = 'https://cdn.jsdelivr.net/npm/jquery-validation@1.21.0/LICENSE.md' }

    @{ d = 'wwwroot/lib/jquery-validation-unobtrusive/jquery.validate.unobtrusive.min.js'; u = 'https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/dist/jquery.validate.unobtrusive.min.js' }
    @{ d = 'wwwroot/lib/jquery-validation-unobtrusive/LICENSE.txt';                        u = 'https://cdn.jsdelivr.net/npm/jquery-validation-unobtrusive@4.0.0/LICENSE.txt' }
)

Write-Host "Descargando $($archivos.Count) archivos a wwwroot/lib/ ..." -ForegroundColor Cyan
$fallos = 0

foreach ($a in $archivos) {
    $carpeta = Split-Path $a.d -Parent
    if (-not (Test-Path $carpeta)) { New-Item -ItemType Directory -Path $carpeta -Force | Out-Null }
    try {
        Invoke-WebRequest -Uri $a.u -OutFile $a.d -UseBasicParsing
        Write-Host ("  OK   {0,-78} {1,8:N1} KB" -f $a.d, ((Get-Item $a.d).Length / 1KB))
    }
    catch {
        # Los .map y los LICENSE son opcionales: si alguno falla, el sitio funciona igual.
        $fallos++
        Write-Warning ("  FALLO  {0}  ->  {1}" -f $a.u, $_.Exception.Message)
    }
}

$total = [math]::Round(((Get-ChildItem wwwroot/lib -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 2)
Write-Host ""
Write-Host "Listo. wwwroot/lib ocupa $total MB. Fallos: $fallos." -ForegroundColor Green
Write-Host "SIGUIENTE PASO OBLIGATORIO: dotnet build  (MapStaticAssets solo sirve lo que existia al compilar)." -ForegroundColor Yellow
