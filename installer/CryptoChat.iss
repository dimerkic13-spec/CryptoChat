; Установщик CryptoChat (Inno Setup 6).
; Собирается скриптом build_installer.ps1 из корня проекта — вручную запускать не нужно.
; В установку входит только то, что нужно для работы: CryptoChat.exe (.NET и ONNX Runtime внутри),
; база знаний (content), словари морфологии (dicts) и модель для поиска по смыслу (models).
; Python, PyTorch, исходники и тесты не устанавливаются.
;
; По умолчанию ставится для текущего пользователя, права администратора не нужны:
;   %LocalAppData%\Programs\CryptoChat
; Если запустить установщик от администратора, он предложит поставить для всех пользователей (Program Files).
; Журналы бесед, база бесед и secrets.json хранятся отдельно — в %LocalAppData%\CryptoChat — и при удалении
; программы не удаляются.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
AppId={{8F3C2B6E-5A41-4C7D-9E2B-0D7A1C3E4F59}
AppName=CryptoChat
AppVersion={#AppVersion}
AppVerName=CryptoChat {#AppVersion}
AppPublisher=CryptoChat
AppComments=Помощник КриптоПро: ответы на типовые вопросы клиентов по базе знаний
DefaultDirName={autopf}\CryptoChat
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=CryptoChat-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\CryptoChat.exe
UninstallDisplayName=CryptoChat {#AppVersion}
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; При обновлении сначала убираем прошлые базу знаний, словари и модель — чтобы не осталось лишних файлов.
Type: filesandordirs; Name: "{app}\content"
Type: filesandordirs; Name: "{app}\dicts"
Type: filesandordirs; Name: "{app}\models"

[Files]
Source: "{#SourceDir}\CryptoChat.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\content\*"; DestDir: "{app}\content"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\dicts\*"; DestDir: "{app}\dicts"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\models\*"; DestDir: "{app}\models"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Папка данных пользователя: журналы, база бесед, secrets.json.
Name: "{localappdata}\CryptoChat"

[Icons]
Name: "{autoprograms}\CryptoChat\CryptoChat"; Filename: "{app}\CryptoChat.exe"; WorkingDir: "{app}"
Name: "{autoprograms}\CryptoChat\Статистика CryptoChat"; Filename: "{cmd}"; Parameters: "/k """"{app}\CryptoChat.exe"" --stats"""; WorkingDir: "{app}"
Name: "{autoprograms}\CryptoChat\Журналы CryptoChat"; Filename: "{localappdata}\CryptoChat"
Name: "{autoprograms}\CryptoChat\Удалить CryptoChat"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CryptoChat"; Filename: "{app}\CryptoChat.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CryptoChat.exe"; Description: "Запустить CryptoChat"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
