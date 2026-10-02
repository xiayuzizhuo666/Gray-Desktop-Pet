param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'out' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$version = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'version.json') | ConvertFrom-Json
$exePath = Join-Path $repoRoot 'GrayPet.exe'
if (-not (Test-Path -LiteralPath $exePath)) { & (Join-Path $repoRoot 'build.ps1') }
$assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($exePath).Version
if ("$($assemblyVersion.Major).$($assemblyVersion.Minor).$($assemblyVersion.Build)" -ne $version.version) { throw 'Executable/product version mismatch' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$buildRoot = Join-Path $OutputDirectory ('msi-work-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildRoot | Out-Null
$msiPath = Join-Path $buildRoot "Gray-Desktop-Pet-$($version.version)-Setup.msi"
$files = @(
    @{ Id='GrayPetExe'; Path='GrayPet.exe'; Component='App'; Name='GrayPet.exe'; Version=$assemblyVersion.ToString() },
    @{ Id='Config'; Path='GrayPet.exe.config'; Component='App'; Name='GRAYCF~1.CFG|GrayPet.exe.config' },
    @{ Id='Frames'; Path='frames.json'; Component='App'; Name='frames.json' },
    @{ Id='Readme'; Path='README.md'; Component='App'; Name='README.md' },
    @{ Id='Notice'; Path='NOTICE.md'; Component='App'; Name='NOTICE.md' },
    @{ Id='Sprites'; Path='assets\spritesheet-extended.png'; Component='Assets'; Name='SPRITE~1.PNG|spritesheet-extended.png' },
    @{ Id='Manifest'; Path='assets\manifest.json'; Component='Assets'; Name='MANIFE~1.JSO|manifest.json' }
)
$ddf = @('.OPTION EXPLICIT','.Set Cabinet=ON','.Set Compress=ON','.Set CompressionType=MSZIP','.Set CabinetNameTemplate=payload.cab',('.Set DiskDirectoryTemplate="' + $buildRoot + '"'),'.Set MaxDiskSize=0')
foreach ($file in $files) {
    $file.Source = Join-Path $repoRoot $file.Path
    if (-not (Test-Path -LiteralPath $file.Source)) { throw "Missing payload: $($file.Path)" }
    $ddf += '"' + $file.Source + '" ' + $file.Id
}
$ddfPath = Join-Path $buildRoot 'payload.ddf'
# makecab consumes the current Windows ANSI encoding, including non-ASCII workspace paths.
[IO.File]::WriteAllLines($ddfPath,$ddf,[Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage))
Push-Location $buildRoot
try { & "$env:WINDIR\System32\makecab.exe" /F $ddfPath | Out-Null; if ($LASTEXITCODE -ne 0) { throw 'makecab failed' } } finally { Pop-Location }

function Call-Com($Object,[string]$Method,[object[]]$Arguments=@()) {
    $plainArgs = [object[]]::new($Arguments.Count)
    for ($i=0; $i -lt $Arguments.Count; $i++) { $plainArgs[$i] = $Arguments[$i].PSObject.BaseObject }
    $Object.GetType().InvokeMember($Method,[Reflection.BindingFlags]::InvokeMethod,$null,$Object,$plainArgs)
}
function Set-Com($Object,[string]$Property,[object[]]$Arguments) {
    $plainArgs = [object[]]::new($Arguments.Count)
    for ($i=0; $i -lt $Arguments.Count; $i++) { $plainArgs[$i] = $Arguments[$i].PSObject.BaseObject }
    $Object.GetType().InvokeMember($Property,[Reflection.BindingFlags]::SetProperty,$null,$Object,$plainArgs)
}
function Get-Com($Object,[string]$Property,[object[]]$Arguments=@()) {
    $plainArgs = [object[]]::new($Arguments.Count)
    for ($i=0; $i -lt $Arguments.Count; $i++) { $plainArgs[$i] = $Arguments[$i].PSObject.BaseObject }
    $Object.GetType().InvokeMember($Property,[Reflection.BindingFlags]::GetProperty,$null,$Object,$plainArgs)
}
function Sql([string]$Statement) {
    $view = Call-Com $script:database 'OpenView' @($Statement)
    try { Call-Com $view 'Execute' } finally { Call-Com $view 'Close'; [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}
function SqlValue($Value) {
    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [int] -or $Value -is [long]) { return [string]$Value }
    return "'" + ([string]$Value).Replace("'","''") + "'"
}
function Row([string]$Table,[object[]]$Values) {
    $columns = @{
        Property='Property,Value'; Directory='Directory,Directory_Parent,DefaultDir';
        Component='Component,ComponentId,Directory_,Attributes,Condition,KeyPath';
        Feature='Feature,Feature_Parent,Title,Description,Display,Level,Directory_,Attributes';
        FeatureComponents='Feature_,Component_'; File='File,Component_,FileName,FileSize,Version,Language,Attributes,Sequence';
        Media='DiskId,LastSequence,DiskPrompt,Cabinet,VolumeLabel,Source'; Registry='Registry,Root,Key,Name,Value,Component_';
        Shortcut='Shortcut,Directory_,Name,Component_,Target,Arguments,Description,Hotkey,Icon_,IconIndex,ShowCmd,WkDir';
        RemoveFile='FileKey,Component_,FileName,DirProperty,InstallMode'; LaunchCondition='Condition,Description';
        Upgrade='UpgradeCode,VersionMin,VersionMax,Language,Attributes,Remove,ActionProperty';
        Dialog='Dialog,HCentering,VCentering,Width,Height,Attributes,Title,Control_First,Control_Default,Control_Cancel';
        Control='Dialog_,Control,Type,X,Y,Width,Height,Attributes,Property,Text,Control_Next,Help';
        ControlEvent='Dialog_,Control_,Event,Argument,Condition,Ordering'; TextStyle='TextStyle,FaceName,Size,Color,StyleBits'
    }
    $names = if ($Table -like '*Sequence') { 'Action,Condition,Sequence' } else { $columns[$Table] }
    $quoted = ($names.Split(',') | ForEach-Object { '`' + $_ + '`' }) -join ','
    Sql ('INSERT INTO `' + $Table + '` (' + $quoted + ') VALUES (' + (($Values | ForEach-Object { SqlValue $_ }) -join ',') + ')')
}
$installer = New-Object -ComObject WindowsInstaller.Installer
$script:database = Call-Com $installer 'OpenDatabase' @($msiPath,3)
try {
    $schemas = @(
        'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) LOCALIZABLE PRIMARY KEY `Property`)',
        'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)',
        'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)',
        'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)',
        'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)',
        'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)',
        'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)',
        'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)',
        'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(72) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)',
        'CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)',
        'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)',
        'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)',
        'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)',
        'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(50), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)',
        'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)',
        'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)'
    )
    foreach ($schema in $schemas) { Sql $schema }
    foreach ($sequence in @('InstallExecuteSequence','InstallUISequence','AdminExecuteSequence','AdminUISequence')) {
        Sql ('CREATE TABLE `' + $sequence + '` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)')
    }
    $properties = [ordered]@{
        ProductName='Gray Desktop Pet'; ProductVersion=$version.version; ProductCode=$version.productCode;
        UpgradeCode=$version.upgradeCode; Manufacturer='Gray Desktop Pet'; ProductLanguage='1033';
        INSTALLLEVEL='1'; ARPNOMODIFY='1'; ARPURLINFOABOUT='https://github.com/xiayuzizhuo666/Gray-Desktop-Pet';
        ARPCOMMENTS='A local desktop companion. Per-user installation.'; DefaultUIFont='Normal';
        SecureCustomProperties='OLDERVERSION;NEWERVERSION'; REBOOT='ReallySuppress'
    }
    foreach ($pair in $properties.GetEnumerator()) { Row 'Property' @($pair.Key,$pair.Value) }
    Row 'LaunchCondition' @('NOT ALLUSERS','This package installs for the current user only. Do not set ALLUSERS.')
    Row 'LaunchCondition' @('NOT NEWERVERSION','A newer Gray Desktop Pet version is already installed.')
    Row 'Upgrade' @($version.upgradeCode,$null,$version.version,$null,0,$null,'OLDERVERSION')
    Row 'Upgrade' @($version.upgradeCode,$version.version,$null,$null,258,$null,'NEWERVERSION')
    Row 'Directory' @('TARGETDIR',$null,'SourceDir')
    Row 'Directory' @('LocalAppDataFolder','TARGETDIR','.')
    Row 'Directory' @('UserPrograms','LocalAppDataFolder','Programs')
    Row 'Directory' @('INSTALLDIR','UserPrograms','GRAYDE~1|GrayDesktopPet')
    Row 'Directory' @('ASSETDIR','INSTALLDIR','assets')
    Row 'Directory' @('ProgramMenuFolder','TARGETDIR','.')
    Row 'Directory' @('APPMENU','ProgramMenuFolder','GRAYDE~1|Gray Desktop Pet')
    Row 'Component' @('App',$version.appComponent,'INSTALLDIR',4,$null,'AppRegistry')
    Row 'Component' @('Assets',$version.assetsComponent,'ASSETDIR',4,$null,'AssetsRegistry')
    Row 'Component' @('Menu',$version.menuComponent,'APPMENU',4,$null,'MenuRegistry')
    Row 'Feature' @('Main',$null,'Gray Desktop Pet','Desktop companion',1,1,'INSTALLDIR',0)
    foreach ($component in @('App','Assets','Menu')) {
        Row 'FeatureComponents' @('Main',$component)
        Row 'Registry' @(($component+'Registry'),1,'Software\GrayDesktopPet',($component+'Version'),$version.version,$component)
    }
    $index=0
    foreach ($file in $files) {
        $index++
        Row 'File' @($file.Id,$file.Component,$file.Name,[int](Get-Item -LiteralPath $file.Source).Length,$file.Version,$null,16384,$index)
    }
    Row 'Media' @(1,$index,$null,'#payload.cab',$null,$null)
    Row 'Shortcut' @('LaunchGray','APPMENU','GRAYDE~1|Gray Desktop Pet','Menu','[#GrayPetExe]',$null,'Start Gray Desktop Pet',$null,$null,$null,1,'INSTALLDIR')
    foreach ($entry in @(@('CleanTest','test-results.txt'),@('CleanSmoke','smoke-test-results.txt'),@('CleanRender','window-render.png'))) {
        Row 'RemoveFile' @($entry[0],'App',$entry[1],'INSTALLDIR',2)
    }
    Row 'RemoveFile' @('RemoveAppFolder','App',$null,'INSTALLDIR',2)
    Row 'RemoveFile' @('RemoveAssetsFolder','Assets',$null,'ASSETDIR',2)
    Row 'RemoveFile' @('RemoveMenuFolder','Menu',$null,'APPMENU',2)
    $actions=[ordered]@{
        FindRelatedProducts=25; LaunchConditions=100; ValidateProductID=700; CostInitialize=800; FileCost=900; CostFinalize=1000;
        InstallValidate=1400; InstallInitialize=1500; RemoveExistingProducts=1501; ProcessComponents=1600;
        UnpublishFeatures=1800; RemoveShortcuts=3200; RemoveFiles=3500; InstallFiles=4000; CreateShortcuts=4500;
        RemoveRegistryValues=2600; WriteRegistryValues=5000; RegisterUser=6000; RegisterProduct=6100;
        PublishFeatures=6300; PublishProduct=6400; InstallFinalize=6600
    }
    foreach ($pair in $actions.GetEnumerator()) { Row 'InstallExecuteSequence' @($pair.Key,$null,[int]$pair.Value) }
    foreach ($sequence in @('InstallUISequence','AdminUISequence')) {
        foreach ($entry in @(@('FindRelatedProducts',25),@('LaunchConditions',100),@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('ExecuteAction',1300))) {
            Row $sequence @($entry[0],$null,$entry[1])
        }
    }
    foreach ($entry in @(@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('InstallValidate',1400),@('InstallInitialize',1500),@('InstallAdminPackage',3900),@('InstallFiles',4000),@('InstallFinalize',6600))) {
        Row 'AdminExecuteSequence' @($entry[0],$null,$entry[1])
    }
    # Minimal full UI; /qb and /qn use the standard engine path for unattended testing.
    Row 'TextStyle' @('Normal','Tahoma',9,0,0)
    foreach ($dialog in @('Welcome','Maintenance','Complete')) {
        $cancelControl=if ($dialog -eq 'Complete') {'Accept'} else {'Cancel'}
        Row 'Dialog' @($dialog,50,50,370,220,3,'[ProductName] [ProductVersion]','Accept','Accept',$cancelControl)
        $message = switch ($dialog) {
            'Welcome' { 'Install Gray for this Windows user. No administrator account or startup entry is required. Requires .NET Framework 4.8. Close a running Gray before proceeding.' }
            'Maintenance' { 'Gray is installed. Choose Remove to uninstall it. Exit the running pet before uninstalling.' }
            'Complete' { 'The operation completed. Launch Gray Desktop Pet from the Start menu after installation. Use Installed apps in Windows Settings to uninstall.' }
        }
        Row 'Control' @($dialog,'Description','Text',20,25,330,100,3,$null,$message,$null,$null)
        $label = switch ($dialog) { 'Welcome' {'Install'} 'Maintenance' {'Remove'} 'Complete' {'Finish'} }
        Row 'Control' @($dialog,'Accept','PushButton',210,180,65,20,3,$null,$label,$cancelControl,$null)
        if ($dialog -ne 'Complete') {
            Row 'Control' @($dialog,'Cancel','PushButton',285,180,65,20,3,$null,'Cancel','Accept',$null)
            Row 'ControlEvent' @($dialog,'Cancel','EndDialog','Exit','1',1)
        }
        if ($dialog -eq 'Maintenance') { Row 'ControlEvent' @($dialog,'Accept','[REMOVE]','ALL','1',1) }
        Row 'ControlEvent' @($dialog,'Accept','EndDialog','Return','1',2)
    }
    Row 'InstallUISequence' @('Welcome','NOT Installed',1100)
    Row 'InstallUISequence' @('Maintenance','Installed AND NOT REMOVE AND NOT REINSTALL',1101)
    Row 'InstallUISequence' @('Complete',$null,-1)
    $stream = Call-Com $installer 'CreateRecord' @(2)
    Set-Com $stream 'StringData' @(1,'payload.cab')
    Call-Com $stream 'SetStream' @(2,(Join-Path $buildRoot 'payload.cab'))
    $view = Call-Com $script:database 'OpenView' @('INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)')
    Call-Com $view 'Execute' @($stream)
    Call-Com $view 'Close'
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($stream)
    $summary = Get-Com $script:database 'SummaryInformation' @(20)
    foreach ($entry in @(@(1,1252),@(2,'Installation Database'),@(3,'Gray Desktop Pet'),@(4,'Gray Desktop Pet'),@(5,'Installer'),@(7,'Intel;1033'),@(9,[guid]::NewGuid().ToString('B').ToUpperInvariant()),@(14,400),@(15,10),@(18,'Gray native MSI builder'),@(19,2))) {
        Set-Com $summary 'Property' @($entry[0],$entry[1])
    }
    Call-Com $summary 'Persist'
    Call-Com $script:database 'Commit'
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($script:database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
$destination=Join-Path $OutputDirectory ([IO.Path]::GetFileName($msiPath))
if (Test-Path -LiteralPath $destination) {
    $archive=Join-Path $buildRoot 'previous-output.msi'
    Copy-Item -LiteralPath $destination -Destination $archive
}
Copy-Item -LiteralPath $msiPath -Destination $destination -Force
Write-Output "Built $destination"
Get-FileHash -LiteralPath $destination -Algorithm SHA256
