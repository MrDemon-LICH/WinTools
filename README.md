# WinTools

Herramientas de optimización del sistema Windows desarrolladas en C# con WPF.

## 📋 Tabla de Contenido

- [Características](#características)
- [Instalación](#instalación)
- [Compilación](#compilación)
- [Desarrollo](#desarrollo)
- [Estructura del Proyecto](#estructura-del-proyecto)

## ⚡ Compilación Rápida

```bash
# Crear instalador EXE
.\build.ps1
```

## Características

- 🖥️ **Monitor de Sistema**: RAM, CPU, GPU, temperatura y procesos en tiempo real
- 🧹 **Limpieza del Sistema**: Eliminación de archivos temporales, vaciado de papelera
- ⚡ **Optimización de RAM**: Liberación de memoria del sistema
- 🌐 **Limpieza DNS**: Cache de resolución DNS
- 📦 **Cache Windows Update**: Limpieza de archivos de actualización
- 🎨 **Tema Oscuro**: Interfaz moderna con diseño oscuro
- 📌 **Widget RAM**: Widget flotante para liberación rápida de RAM
- 🔄 **Inicio Automático**: Opción para iniciar con Windows
- 📱 **Bandeja del Sistema**: Minimizar a bandeja con notificaciones

## Requisitos

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime (incluido en versiones portable e instalador)

## Instalación

### 🚀 Instalador EXE

**Archivo**: `WinTools.Installer.exe`

**Proceso de Instalación**:
1. Descarga el archivo `.exe`
2. Haz doble clic y ejecuta como administrador
3. **El instalador mostrará la ruta de instalación**: `C:\Program Files\WinTools\`
4. Puedes cambiar la ruta si lo deseas
5. Sigue el asistente de instalación
6. Se crean accesos directos en menú Inicio y escritorio

**Características**:
- ✅ Instalador profesional con desinstalador completo
- ✅ Muestra la ruta de instalación durante el proceso
- ✅ Cierra automáticamente la aplicación durante la desinstalación
- ✅ Integración completa con Windows
- ✅ Accesos directos en menú Inicio y escritorio
4. Puede ejecutarse desde USB o cualquier ubicación

**Ventajas**:
- ✅ No requiere instalación
- ✅ Completamente portable
- ✅ Sin residuos en el sistema
- ✅ Ideal para múltiples PCs o USB

### ✅ Verificación de Instalación

Después de instalar/ejecutar, verifica que:

1. **La aplicación inicia correctamente**
2. **El monitor de sistema muestra datos** (CPU, RAM, etc.)
3. **Las funciones de limpieza funcionan** (botones responden)
4. **La opción "Iniciar con Windows" funciona** (si está activada)

**Nota**: Ambas versiones incluyen el runtime de .NET 8, por lo que no requieren instalación adicional de .NET en los PCs destino.

## Compilación

### Requisitos

- **.NET 8.0 SDK**: [Descargar](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Inno Setup**: [Descargar](https://jrsoftware.org/isdl.php)

### Compilación Automática

```bash
# Crear instalador EXE
.\build.ps1
```

**Resultado**: `publish\WinTools.Installer.exe`

### Solución de Problemas

**Error "WiX Toolset no encontrado"**:
```bash
# Instala WiX Toolset desde: https://wixtoolset.org/releases/
# Asegúrate de que esté en PATH o usa rutas absolutas
```

**Error de .NET SDK**:
```bash
# Verifica instalación: dotnet --version
# Debe mostrar 8.0.x
```

**Error de publicación**:
```bash
# Limpia el proyecto primero
dotnet clean
dotnet restore
```

## Estructura del Proyecto

- `WinTools.csproj`: Configuración del proyecto (.NET 8, self-contained)
- `MainWindow.xaml/cs`: Interfaz principal y lógica de negocio
- `App.xaml/cs`: Configuración de la aplicación
- `WinTools.Installer.iss`: Script de Inno Setup para el instalador
- `build.ps1`: Script de compilación automatizado

## Tecnologías Utilizadas

- **C# .NET 8.0**: Framework principal
- **WPF (Windows Presentation Foundation)**: Interfaz de usuario
- **Inno Setup**: Creación de instaladores EXE
- **Performance Counters**: Monitorización del sistema
- **P/Invoke**: Llamadas a APIs nativas de Windows

## Desarrollo

### Entorno de Desarrollo

**Requisitos**:
- Visual Studio 2022 o Visual Studio Code
- .NET 8.0 SDK
- Inno Setup (para crear instaladores)

**Configuración Inicial**:
```bash
# Verificar .NET SDK
dotnet --version  # Debe mostrar 8.0.x

# Restaurar dependencias
dotnet restore

# Compilar en modo debug
dotnet build

# Ejecutar aplicación
dotnet run
```

### Flujo de Trabajo

1. **Desarrollo**: Modifica código en Visual Studio
2. **Pruebas**: Ejecuta con `dotnet run`
3. **Compilación**: Usa `.\build.ps1` para crear el instalador
4. **Distribución**: El instalador está en `publish\WinTools.Installer.exe`

## Notas de Distribución

- La aplicación es **self-contained**: incluye el runtime de .NET 8
- No requiere instalación de .NET en los PCs destino
- Compatible con Windows 10/11 x64
- Instalador incluye desinstalador completo y accesos directos
- Desinstalador cierra automáticamente la aplicación si está ejecutándose

## 🆘 Solución de Problemas

### Problemas Comunes

**"La aplicación no inicia"**
- Verifica que estés en Windows 10/11 x64
- Asegúrate de que los archivos no estén bloqueados por Windows Defender
- Ejecuta como administrador si hay problemas de permisos

**"Error al compilar"**
```bash
dotnet clean
dotnet restore
dotnet build
```

**"Inno Setup no encontrado"**
- Descarga Inno Setup desde: https://jrsoftware.org/isdl.php
- Instala con las opciones por defecto
- Reinicia PowerShell después de instalar

**"Archivos de salida no encontrados"**
- Verifica que la compilación terminó sin errores
- Busca en `bin\Release\net8.0-windows\win-x64\publish\`

**"El desinstalador deja archivos"**
- El desinstalador automáticamente cierra la aplicación si está ejecutándose
- Si la aplicación no responde, se fuerza el cierre para permitir la eliminación completa
- En casos excepcionales, algunos archivos temporales pueden quedar (muy raro)

### Logs de Depuración

Para más información durante la compilación:
```bash
# Compilación detallada
dotnet publish -c Release -r win-x64 --self-contained true -v detailed

# Ver logs del script
.\build.ps1 -Portable -Verbose
```

## 📞 Soporte

Si encuentras problemas:

1. Verifica esta documentación
2. Revisa los [issues](https://github.com/tu-repo/WinTools/issues) del proyecto
3. Crea un nuevo issue con:
   - Versión de Windows
   - Versión de .NET SDK (`dotnet --version`)
   - Error completo y pasos para reproducirlo

## 📄 Licencia

Este proyecto es de código abierto. Modifícalo y distribúyelo según tus necesidades.
