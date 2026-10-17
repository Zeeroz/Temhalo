; Unicode NSIS 3.13. Product payload is the validated, self-contained ZIP.
; Build only through tools/Build-Installer.ps1, which generates file inventories.
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
!include "Sections.nsh"

!ifdef TEST_ROOT
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Temhalo.Installer.Test.${TEST_ID}"
  !define SETUP_MUTEX "Local\Temhalo.Setup.Test.${TEST_ID}"
!else
  !define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Temhalo"
  !define SETUP_MUTEX "Local\Temhalo.Setup"
!endif

Name "Temhalo"
Caption "Installation de Temhalo"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\Temhalo"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel user
ManifestDPIAware true
ManifestLongPathAware true
SetCompressor /SOLID lzma
SetCompressorDictSize 16
SetDatablockOptimize on
SetOverwrite on
ShowInstDetails show
ShowUninstDetails show
BrandingText "Votre live. Votre tempo."
VIProductVersion "${NUMERIC_VERSION}"
VIAddVersionKey /LANG=1036 "ProductName" "Temhalo"
VIAddVersionKey /LANG=1036 "CompanyName" "Temhalo"
VIAddVersionKey /LANG=1036 "FileDescription" "Installation de Temhalo pour Windows x64"
VIAddVersionKey /LANG=1036 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1036 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1036 "LegalCopyright" "Temhalo"

!define MUI_ICON "${BRAND_ICON}"
!define MUI_UNICON "${BRAND_ICON}"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP "${ARTWORK_DIR}\header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "${ARTWORK_DIR}\welcome.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "${ARTWORK_DIR}\welcome.bmp"
!define MUI_BGCOLOR "0D1218"
!define MUI_TEXTCOLOR "F1F5F9"
!define MUI_INSTFILESPAGE_COLORS "F1F5F9 151D26"

!define MUI_WELCOMEPAGE_TITLE "Installez votre tempo."
!define MUI_WELCOMEPAGE_TEXT "Temhalo ${PRODUCT_VERSION}$\r$\nContrôle du délai de diffusion$\r$\n$\r$\nL'application sera installée pour votre compte Windows, avec une entrée dans le menu Démarrer.$\r$\n$\r$\nFermez Temhalo avant une mise à jour, une fois votre diffusion terminée.$\r$\n$\r$\nVos réglages et vos clés chiffrées seront conservés."
!define MUI_PAGE_CUSTOMFUNCTION_PRE WelcomePagePre
!insertmacro MUI_PAGE_WELCOME
!define MUI_COMPONENTSPAGE_TEXT_TOP "Le menu Démarrer est inclus. Choisissez le raccourci Bureau et, si Stream Deck est détecté, l'installation ou la mise à jour du plugin Temhalo."
!define MUI_COMPONENTSPAGE_NODESC
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_TITLE "Temhalo est prêt."
!define MUI_FINISHPAGE_TEXT "Lancez Temhalo depuis le menu Démarrer ou votre Bureau.$\r$\n$\r$\nSi vous avez choisi le plugin, son installation s'ouvre dans Stream Deck : terminez les éventuelles confirmations dans cette application.$\r$\n$\r$\nMediaMTX et FFmpeg seront téléchargés et vérifiés au premier démarrage du relais."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Ouvrir Temhalo"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchTemhalo
!define MUI_PAGE_CUSTOMFUNCTION_SHOW FinishPageShow
!insertmacro MUI_PAGE_FINISH

!define MUI_UNWELCOMEPAGE_TITLE "Désinstaller Temhalo"
!define MUI_UNWELCOMEPAGE_TEXT "Cette opération retirera l'application, les raccourcis et son entrée dans les applications installées.$\r$\n$\r$\nLes réglages, clés chiffrées, outils téléchargés, journaux et enregistrements de votre profil seront conservés.$\r$\n$\r$\nTerminez votre diffusion et fermez Temhalo avant de continuer."
!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!define MUI_UNFINISHPAGE_TEXT "L'application et ses raccourcis ont été retirés.$\r$\n$\r$\nVos données restent dans %LOCALAPPDATA%\Temhalo. Une réinstallation pourra les réutiliser."
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "French"

Var DesktopDirectory
Var MenuDirectory
Var ShortcutArguments
Var PreviousInstallDirectory
Var GuardedPath
Var SetupMutex
Var ProbeHandle
Var UpdateParentPid
Var StreamDeckAvailable
!ifdef TEST_ROOT
  ; Isolated installer tests simulate detection and ShellExecute. They never
  ; install into the real Stream Deck profile, including on a developer PC.
  Var TestStreamDeckMode
  Var TestStreamDeckUi
!endif

; Functions are shared with the uninstaller. Never recursively remove $INSTDIR.
!macro SharedFunctions PREFIX
Function ${PREFIX}Configure
  SetShellVarContext current
  SetRegView 64
  !ifdef TEST_ROOT
    StrCpy $DesktopDirectory "${TEST_ROOT}\Desktop"
    StrCpy $MenuDirectory "${TEST_ROOT}\Start Menu\Temhalo"
    StrCpy $ShortcutArguments '--data-directory "${TEST_ROOT}\profile data"'
  !else
    StrCpy $DesktopDirectory "$DESKTOP"
    StrCpy $MenuDirectory "$SMPROGRAMS\Temhalo"
    StrCpy $ShortcutArguments ""
  !endif
  ReadRegStr $PreviousInstallDirectory HKCU "${UNINSTALL_KEY}" "InstallLocation"
  System::Call 'kernel32::CreateMutexW(p 0, i 0, w "${SETUP_MUTEX}") p .r0 ?e'
  Pop $1
  StrCpy $SetupMutex $0
  ${If} $1 = 183
    MessageBox MB_OK|MB_ICONEXCLAMATION "Une installation ou désinstallation de Temhalo est déjà en cours." /SD IDOK
    SetErrorLevel 1618
    Quit
  ${EndIf}
FunctionEnd

Function ${PREFIX}GuardDirectory
  ; Check every existing ancestor: a junction must never redirect cleanup.
  StrCpy $0 $GuardedPath
  directory_loop_${PREFIX}:
    System::Call 'kernel32::GetFileAttributesW(w r0) i .r1'
    ${If} $1 != -1
      IntOp $2 $1 & 0x400
      ${If} $2 != 0
        MessageBox MB_OK|MB_ICONSTOP "Le dossier contient une jonction ou un lien symbolique :$\r$\n$0$\r$\nChoisissez un dossier local ordinaire." /SD IDOK
        SetErrorLevel 87
        Quit
      ${EndIf}
    ${EndIf}
    ${GetParent} "$0" $1
    StrCmp $0 $1 directory_done_${PREFIX}
    StrCmp $1 "" directory_done_${PREFIX}
    StrCpy $0 $1
    Goto directory_loop_${PREFIX}
  directory_done_${PREFIX}:
FunctionEnd

Function ${PREFIX}ValidateInstallDirectory
  ; NSIS GetFullPathName requires the parent directory to exist. The Windows
  ; API also normalizes a fresh installation path, without creating anything.
  System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r0, p 0) i .r1'
  ${If} $1 = 0
  ${OrIf} $1 >= ${NSIS_MAX_STRLEN}
    MessageBox MB_OK|MB_ICONSTOP "Le chemin d'installation est invalide ou trop long." /SD IDOK
    SetErrorLevel 87
    Quit
  ${EndIf}
  StrCpy $INSTDIR $0
  StrLen $0 "$INSTDIR"
  IntOp $0 $0 + ${MAX_RELATIVE_PATH}
  IntOp $0 $0 + 1
  ${If} $0 >= 260
    ReadRegDWORD $1 HKLM "SYSTEM\CurrentControlSet\Control\FileSystem" "LongPathsEnabled"
    ${If} $1 != 1
      MessageBox MB_OK|MB_ICONSTOP "Ce chemin dépasse la limite de fichiers de Windows.$\r$\nUtilisez un chemin plus court avec /D=chemin\Temhalo." /SD IDOK
      SetErrorLevel 87
      Quit
    ${EndIf}
  ${EndIf}
  ${GetFileName} "$INSTDIR" $0
  ${If} $0 != "Temhalo"
    MessageBox MB_OK|MB_ICONSTOP "Le dossier d'installation doit être un dossier dédié nommé Temhalo." /SD IDOK
    SetErrorLevel 87
    Quit
  ${EndIf}
  StrCpy $GuardedPath "$INSTDIR"
  Call ${PREFIX}GuardDirectory
FunctionEnd

Function ${PREFIX}ProbeOwnedFile
  System::Call 'kernel32::GetFileAttributesW(w "$GuardedPath") i .r0'
  ${If} $0 = -1
    Return
  ${EndIf}
  IntOp $1 $0 & 0x410
  ${If} $1 != 0
    MessageBox MB_OK|MB_ICONSTOP "Un fichier attendu est remplacé par un dossier ou un lien :$\r$\n$GuardedPath" /SD IDOK
    SetErrorLevel 87
    Quit
  ${EndIf}
  probe_retry_${PREFIX}:
    ; OPEN_EXISTING, GENERIC_WRITE | DELETE, no sharing. No bytes are changed.
    ; Windows refuses this probe for a mapped executable or loaded DLL.
    System::Call 'kernel32::CreateFileW(w "$GuardedPath", i 0x40010000, i 0, p 0, i 3, i 128, p 0) p .r0'
    StrCpy $ProbeHandle $0
    ${If} $ProbeHandle = -1
      MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Un fichier de Temhalo est utilisé ou non modifiable.$\r$\n$\r$\nTerminez votre diffusion et fermez Temhalo, puis choisissez Réessayer.$\r$\n$\r$\n$GuardedPath" /SD IDCANCEL IDRETRY probe_retry_${PREFIX}
      SetErrorLevel 32
      Quit
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p $ProbeHandle)'
FunctionEnd
!macroend
!insertmacro SharedFunctions ""
!insertmacro SharedFunctions "un."

; Updates keep the remembered options and skip the repeated introductory page.
Function WelcomePagePre
  ${If} $PreviousInstallDirectory != ""
    Abort
  ${EndIf}
FunctionEnd

Function .onInit
  ; The in-app updater starts the ordinary wizard, then closes Temhalo cleanly.
  ; Wait for that exact process before probing or replacing any application file.
  ${GetParameters} $0
  ${GetOptions} $0 "/WAITPID=" $UpdateParentPid
  ${If} $UpdateParentPid != ""
    IntOp $0 $UpdateParentPid + 0
    ${If} $0 <= 0
      SetErrorLevel 87
      Quit
    ${EndIf}
    System::Call 'kernel32::OpenProcess(i 0x100000, i 0, i r0) p .r1'
    ${If} $1 != 0
      System::Call 'kernel32::WaitForSingleObject(p r1, i 60000) i .r2'
      System::Call 'kernel32::CloseHandle(p r1)'
      ${If} $2 != 0
        MessageBox MB_OK|MB_ICONEXCLAMATION "Temhalo est encore ouvert. Fermez-le après la fin de votre live, puis relancez l’installeur." /SD IDOK
        SetErrorLevel 32
        Quit
      ${EndIf}
    ${EndIf}
  ${EndIf}
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "Temhalo nécessite Windows 10/11 64 bits." /SD IDOK
    SetErrorLevel 1633
    Quit
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_OK|MB_ICONSTOP "Temhalo nécessite Windows 10 version 1809 ou ultérieure." /SD IDOK
    SetErrorLevel 1633
    Quit
  ${EndIf}
  SetRegView 64
  ReadRegStr $0 HKLM "Software\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 17763
    MessageBox MB_OK|MB_ICONSTOP "Temhalo nécessite Windows 10 version 1809 ou ultérieure." /SD IDOK
    SetErrorLevel 1633
    Quit
  ${EndIf}
  Call Configure
  ${If} $PreviousInstallDirectory != ""
    StrCpy $INSTDIR $PreviousInstallDirectory
    ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "NumericVersion"
    ${VersionCompare} "$0" "${NUMERIC_VERSION}" $1
    ${If} $1 = 1
      MessageBox MB_OK|MB_ICONEXCLAMATION "Une version plus récente de Temhalo est déjà installée. Utilisez son installeur pour la réparer." /SD IDOK
      SetErrorLevel 1638
      Quit
    ${EndIf}
  ${EndIf}
  Call ValidateInstallDirectory
  Call ConfigureStreamDeckComponent
FunctionEnd

Function un.onInit
  Call un.Configure
  ${If} $PreviousInstallDirectory != $INSTDIR
    MessageBox MB_OK|MB_ICONSTOP "Cette désinstallation ne correspond pas au dossier enregistré de Temhalo." /SD IDOK
    SetErrorLevel 87
    Quit
  ${EndIf}
  Call un.ValidateInstallDirectory
FunctionEnd

!include "${GENERATED_DIR}\preflight.nsh"

Section "Temhalo et menu Démarrer" SEC_APP
  SectionIn RO
  !insertmacro Preflight ""
  ; Remove only known files retired by earlier releases. Keep user additions.
  ClearErrors
  !include "${GENERATED_DIR}\obsolete.nsh"
  IfErrors setup_failed
  ClearErrors
  !include "${GENERATED_DIR}\payload.nsh"
  IfErrors setup_failed
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors setup_failed
  CreateDirectory "$MenuDirectory"
  SetOutPath "$INSTDIR"
  CreateShortCut "$MenuDirectory\Temhalo.lnk" "$INSTDIR\Temhalo.App.exe" '$ShortcutArguments' "$INSTDIR\Assets\Brand\temhalo.ico" 0 SW_SHOWNORMAL "" "Contrôle du délai de diffusion"
  CreateShortCut "$MenuDirectory\Désinstaller Temhalo.lnk" "$INSTDIR\Uninstall.exe" "" "$INSTDIR\Assets\Brand\temhalo.ico"
  IfErrors setup_failed
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Temhalo"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "NumericVersion" "${NUMERIC_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "Temhalo"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Assets\Brand\temhalo.ico"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${INSTALLED_KIB}
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  IfErrors setup_failed
  Goto setup_done
  setup_failed:
    MessageBox MB_OK|MB_ICONSTOP "Installation incomplète. Vérifiez l'espace disque et les droits du dossier, puis relancez l'installeur." /SD IDOK
    SetErrorLevel 5
    Quit
  setup_done:
SectionEnd

Section "Raccourci sur le Bureau" SEC_DESKTOP
  CreateDirectory "$DesktopDirectory"
  CreateShortCut "$DesktopDirectory\Temhalo.lnk" "$INSTDIR\Temhalo.App.exe" '$ShortcutArguments' "$INSTDIR\Assets\Brand\temhalo.ico" 0 SW_SHOWNORMAL "" "Contrôle du délai de diffusion"
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONEXCLAMATION "Temhalo est installé, mais le raccourci du Bureau n'a pas pu être créé. Utilisez le menu Démarrer." /SD IDOK
    SetErrorLevel 5
  ${EndIf}
SectionEnd

Section /o "Plugin Stream Deck (facultatif)" SEC_STREAMDECK
  DetailPrint "Plugin Temhalo : le paquet sera ouvert dans Stream Deck après installation."
SectionEnd

Function ConfigureStreamDeckComponent
  StrCpy $StreamDeckAvailable 0
  !ifdef TEST_ROOT
    ${GetParameters} $0
    ${GetOptions} $0 "/TESTSD=" $TestStreamDeckMode
    ${GetOptions} $0 "/TESTPLUGINUI=" $TestStreamDeckUi
    ${If} $TestStreamDeckMode == "present"
    ${OrIf} $TestStreamDeckMode == "failure"
      StrCpy $StreamDeckAvailable 1
    ${EndIf}
  !else
    ; Stream Deck registers this package type in HKCR for both registry views.
    ; Let Windows/Elgato handle installation, never edit its plugin/profile files.
    ReadRegStr $0 HKCR ".streamDeckPlugin" ""
    ${If} $0 != ""
      ReadRegStr $1 HKCR "$0\shell\open\command" ""
      ${If} $1 != ""
        StrCpy $StreamDeckAvailable 1
      ${EndIf}
    ${EndIf}
  !endif
  ${If} $StreamDeckAvailable == 1
    ClearErrors
    ReadRegDWORD $0 HKCU "${UNINSTALL_KEY}" "InstallStreamDeckPlugin"
    ${If} ${Errors}
      StrCpy $0 1
    ${EndIf}
    ${If} $0 == 1
      SectionSetFlags ${SEC_STREAMDECK} ${SF_SELECTED}
    ${Else}
      SectionSetFlags ${SEC_STREAMDECK} 0
    ${EndIf}
    !ifdef TEST_ROOT
      ${GetParameters} $0
      ${GetOptions} $0 "/TESTPLUGINCHOICE=" $1
      ${If} $1 == "1"
        SectionSetFlags ${SEC_STREAMDECK} ${SF_SELECTED}
      ${ElseIf} $1 == "0"
        SectionSetFlags ${SEC_STREAMDECK} 0
      ${EndIf}
    !endif
  ${Else}
    SectionSetText ${SEC_STREAMDECK} "Plugin Stream Deck (Stream Deck non détecté)"
    SectionSetFlags ${SEC_STREAMDECK} ${SF_RO}
  ${EndIf}
  !ifdef TEST_ROOT
    WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Component" "Available" "$StreamDeckAvailable"
    SectionGetFlags ${SEC_STREAMDECK} $0
    IntOp $0 $0 & ${SF_SELECTED}
    WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Component" "Selected" "$0"
    WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Status" "pending"
  !endif
FunctionEnd

Function .onInstSuccess
  ; Silent installations never open another application or alter this choice.
  !ifdef TEST_ROOT
    ${If} $TestStreamDeckUi == "1"
      Goto streamdeck_interactive
    ${EndIf}
  !endif
  IfSilent streamdeck_silent streamdeck_interactive
  streamdeck_silent:
    !ifdef TEST_ROOT
      WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Status" "silent"
    !endif
    Return
  streamdeck_interactive:
    ${If} $StreamDeckAvailable != 1
      !ifdef TEST_ROOT
        WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Status" "unavailable"
      !endif
      Return
    ${EndIf}
    SectionGetFlags ${SEC_STREAMDECK} $0
    IntOp $0 $0 & ${SF_SELECTED}
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "InstallStreamDeckPlugin" $0
    ${If} $0 == 0
      !ifdef TEST_ROOT
        WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Status" "not-selected"
      !endif
      Return
    ${EndIf}
    SetOutPath "$INSTDIR"
    ClearErrors
    !ifdef TEST_ROOT
      WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Package" "$INSTDIR\plugins\streamdeck\Temhalo.streamDeckPlugin"
      ${If} $TestStreamDeckMode == "failure"
        WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Status" "failed"
        SetErrors
      ${Else}
        WriteINIStr "${TEST_ROOT}\streamdeck-test.ini" "Handoff" "Status" "opened"
      ${EndIf}
    !else
      ExecShell "open" "$INSTDIR\plugins\streamdeck\Temhalo.streamDeckPlugin" "" SW_SHOWNORMAL
    !endif
    ${If} ${Errors}
      MessageBox MB_OK|MB_ICONINFORMATION "Temhalo est installé. Le plugin n'a pas pu être ouvert dans Stream Deck.$\r$\n$\r$\nInstallez ou ouvrez Stream Deck, puis double-cliquez sur :$\r$\n$INSTDIR\plugins\streamdeck\Temhalo.streamDeckPlugin" /SD IDOK
      ; Plugin handoff is optional; the successful application install stays valid.
      ClearErrors
    ${EndIf}
FunctionEnd

Function LaunchTemhalo
  SetOutPath "$INSTDIR"
  Exec '"$INSTDIR\Temhalo.App.exe" $ShortcutArguments'
FunctionEnd

Function FinishPageShow
  ; The themed Windows checkbox ignores SetCtlColors (NSIS MUI bug #443).
  ; Disable theming only on this control so its label uses the dark-page palette.
  System::Call 'UXTHEME::SetWindowTheme(p $mui.FinishPage.Run, w " ", w " ")'
  SetCtlColors $mui.FinishPage.Run "${MUI_TEXTCOLOR}" "${MUI_BGCOLOR}"
FunctionEnd

Section "Uninstall"
  !insertmacro Preflight "un."
  SetOutPath "$TEMP"
  ClearErrors
  !include "${GENERATED_DIR}\delete-files.nsh"
  IfErrors uninstall_failed
  Delete "$DesktopDirectory\Temhalo.lnk"
  Delete "$MenuDirectory\Temhalo.lnk"
  Delete "$MenuDirectory\Désinstaller Temhalo.lnk"
  IfErrors uninstall_failed
  RMDir "$MenuDirectory"
  ; A copied uninstaller runs from TEMP, so removing its installed file is safe.
  ClearErrors
  Delete "$INSTDIR\Uninstall.exe"
  IfErrors uninstall_failed
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  IfErrors uninstall_failed
  !include "${GENERATED_DIR}\delete-directories.nsh"
  RMDir "$INSTDIR"
  SetErrorLevel 0
  Goto uninstall_done
  uninstall_failed:
    MessageBox MB_OK|MB_ICONSTOP "La désinstallation est incomplète. Fermez Temhalo et relancez la désinstallation. Les données du profil sont conservées." /SD IDOK
    SetErrorLevel 5
    Quit
  uninstall_done:
SectionEnd
