# Script de compilación para WinTools
# Crea el instalador EXE usando Inno Setup

Write-Host "=== Compilacion de WinTools ===" -ForegroundColor Cyan

# Verificar .NET
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "ERROR: .NET SDK no encontrado. Instala .NET 8.0 SDK"
    exit 1
}

# Verificar Inno Setup
$innoPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $innoPath)) {
    Write-Error "ERROR: Inno Setup no esta instalado. Descargalo desde https://jrsoftware.org/isdl.php"
    exit 1
}

Write-Host "Dependencias verificadas correctamente`n" -ForegroundColor Green

# Limpiar archivos anteriores
Write-Host "Limpiando archivos anteriores..." -ForegroundColor Yellow
if (Test-Path "publish") {
    Remove-Item "publish" -Recurse -Force
}
if (Test-Path "bin") {
    Remove-Item "bin" -Recurse -Force
}
if (Test-Path "obj") {
    Remove-Item "obj" -Recurse -Force
}
if (Test-Path "WinTools.exe") {
    Remove-Item "WinTools.exe" -Force
}
Write-Host "Archivos limpiados`n" -ForegroundColor Green

# Compilar aplicación
Write-Host "Compilando aplicacion..." -ForegroundColor Yellow
dotnet build WinTools.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Fallo al compilar la aplicacion"
    exit 1
}
Write-Host "Aplicacion compilada correctamente`n" -ForegroundColor Green

# Crear instalador EXE
Write-Host "Creando instalador EXE..." -ForegroundColor Yellow

# Publicar aplicación como single-file
dotnet publish WinTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Fallo al publicar la aplicacion"
    exit 1
}

# Crear directorio publish si no existe
if (-not (Test-Path "publish")) {
    New-Item -ItemType Directory -Path "publish" | Out-Null
}

# Copiar ejecutable y crear instalador
$sourcePath = "bin\Release\net8.0-windows\win-x64\publish\WinTools.exe"
Copy-Item $sourcePath -Destination "." -Force

# Crear instalador con Inno Setup
& $innoPath "WinTools.Installer.iss"
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Fallo al crear el instalador"
    exit 1
}

Write-Host "Instalador EXE creado: publish\WinTools.Installer.exe" -ForegroundColor Green
Write-Host "`nCompilacion completada exitosamente!" -ForegroundColor Green
