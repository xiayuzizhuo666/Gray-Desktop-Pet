$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot
$sourcePath = $projectPath
if (Test-Path -LiteralPath (Join-Path $projectPath 'source\Program.cs')) { $sourcePath = Join-Path $projectPath 'source' }
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$wpfPath = Join-Path $frameworkPath 'WPF'
$compilerArguments = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',"/out:$projectPath\GrayPet.exe", "/win32manifest:$projectPath\GrayPet.manifest",
    "/reference:$wpfPath\PresentationFramework.dll", "/reference:$wpfPath\PresentationCore.dll", "/reference:$wpfPath\WindowsBase.dll", "/reference:$frameworkPath\System.Xaml.dll", "/reference:$frameworkPath\System.Web.Extensions.dll", "/reference:$frameworkPath\System.Windows.Forms.dll", "/reference:$frameworkPath\System.Drawing.dll",
    "$sourcePath\Core.cs", "$sourcePath\Native.cs", "$sourcePath\PetWindow.cs", "$sourcePath\TrayController.cs", "$sourcePath\Program.cs", "$sourcePath\Tests.cs")
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed ($LASTEXITCODE)" }
Write-Output "Built $projectPath\GrayPet.exe"
