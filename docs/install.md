# MoaView Windows 설치

`MoaView-0.1.4-Setup-x64.exe`를 실행해 설치합니다. Windows 10 1809 이상 또는 Windows 11의 x64/호환 환경용입니다. 관리자 권한 없이 현재 사용자에게 설치됩니다.

- 기본 경로: `%LOCALAPPDATA%\Programs\MoaView`
- 시작 메뉴에 MoaView가 등록됩니다. 바탕 화면 바로가기는 설치 중 선택합니다.
- .NET 8.0.33 Windows Desktop 런타임을 포함하므로 .NET을 따로 설치하지 않습니다.
- Rive 재생에는 WebView2 Runtime이 필요합니다. 설치되지 않았다면 마지막 화면에서 Microsoft WebView2 설치를 선택할 수 있으며 이 단계에는 인터넷이 필요합니다. 설치를 건너뛰어도 다른 형식은 사용할 수 있습니다.
- 설치 완료 화면에서 MoaView 실행이 기본 선택됩니다. MoaView가 실행 중일 때 탐색기에서 파일 하나를 선택하고 Space를 눌러 미리봅니다. 기본 창을 최소화해도 사용할 수 있습니다.
- 파일 하나를 우클릭해 **MoaView로 미리보기**를 선택할 수 있습니다. Windows 11에서는 **추가 옵션 표시** 안에 있습니다. 이 메뉴는 앱을 미리 켜 두지 않아도 사용할 수 있습니다.
- 현재 설치 파일은 코드 서명이 없는 개발 배포본입니다. Windows의 출처 확인 안내가 나타날 수 있습니다.

Windows 설정의 설치된 앱에서 MoaView를 제거하면 탐색기 우클릭 메뉴도 함께 제거됩니다. 개인 설정과 WebView2 캐시는 유지합니다. 이 데이터까지 지우려면 앱 종료 후 `%LOCALAPPDATA%\MoaView`를 삭제합니다. 다른 앱에서도 사용하는 WebView2 Runtime은 함께 제거하지 않습니다.

## 다시 빌드

Windows x64 빌드 환경과 .NET 8 SDK가 필요합니다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1
```

공식 Inno Setup 7.1.1을 `.tools`에 포터블 모드로 준비하고 서명을 확인합니다. WebView2 부트스트래퍼도 Microsoft 공식 배포처에서 내려받아 서명을 검증합니다. 앱을 자체 포함 방식으로 빌드한 뒤 설치 파일과 SHA-256을 `artifacts/installer`에 생성합니다. Inno Setup의 배포·사용 조건은 [공식 안내](https://jrsoftware.org/isinfo.php)를 참고하세요.

`-Version`과 `-RuntimeVersion`으로 버전을 명시할 수 있습니다. `-SkipPublish`는 이미 준비한 배포 폴더를 사용할 때만 지정하며 앱 버전과 포함 런타임을 확인합니다. 무인 설치(`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`)는 마지막 화면의 앱 실행 및 WebView2 설치를 생략하므로 WebView2가 필요하면 별도로 준비합니다.

## Windows 11 기본 우클릭 메뉴

Windows 11 기본 메뉴용 빌드는 `IExplorerCommand` DLL과 서명된 앱 식별 패키지를 함께 설치합니다. Windows가 패키지 등록을 완료하면 파일 하나를 우클릭해 **MoaView로 미리보기**를 바로 선택할 수 있습니다. 등록에 실패한 경우에는 **추가 옵션 표시**의 기존 항목으로 열 수 있습니다. [Microsoft 탐색기 통합 안내](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)

Visual Studio C++ x64 도구, Windows SDK, PowerShell 7이 있는 Windows 빌드 환경에서 다음과 같이 개발용 빌드를 만들 수 있습니다.

```powershell
pwsh -NoProfile -File scripts/Build-ExplorerIdentity.ps1 -Version 0.1.3 -DevelopmentCertificate
powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1 -Version 0.1.4 -ExplorerIdentityDirectory artifacts/explorer-identity/0.1.3
```

개발 빌드에서 생성한 공개 `MoaView-Development.cer`는 해당 빌드의 Windows 패키지 등록을 검증할 때 사용합니다. 설치하려는 Windows가 그 인증서를 신뢰해야 하며 설치 프로그램은 인증서 신뢰 저장소를 변경하지 않습니다. 신뢰된 배포용 서명 인증서가 `CurrentUser\My`에 있으면 첫 명령에서 `-DevelopmentCertificate` 대신 `-SigningThumbprint`로 인증서를 지정할 수 있습니다. [Microsoft 패키지 서명 안내](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps#build-and-sign-the-identity-package)
