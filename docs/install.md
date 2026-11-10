# MoaView Windows 설치

`artifacts/installer/MoaView-0.1.10-Setup-x64.exe`를 실행해 설치합니다. Windows 10 1809 이상 또는 Windows 11의 x64/호환 환경용입니다. 관리자 권한 없이 현재 사용자에게 설치됩니다.

- 기본 경로: `%LOCALAPPDATA%\Programs\MoaView`
- 시작 메뉴에 MoaView가 등록됩니다. 바탕 화면 바로가기는 설치 중 선택합니다.
- .NET 8.0.33 Windows Desktop 런타임을 포함하므로 .NET을 따로 설치하지 않습니다.
- Rive·3D 모델과 SVG·WebP·AVIF·ICO·APNG·BPG·GIF 애니메이션 미리보기에는 WebView2 Runtime이 필요합니다. 설치되지 않았다면 마지막 화면에서 Microsoft WebView2 설치를 선택할 수 있으며 이 단계에는 인터넷이 필요합니다.
- 설치 완료 화면에서 MoaView 실행이 기본 선택됩니다. 기본 창을 닫아도 트레이에서 계속 실행되므로 탐색기에서 파일 하나를 선택하고 Space로 미리볼 수 있습니다. 트레이 아이콘 더블클릭으로 창을 열고, 우클릭 메뉴에서 Space 전환과 **MoaView 종료**를 선택합니다.
- 설치할 때 **Windows 로그인 시 MoaView 자동 실행**을 선택하거나, 앱의 **설정 → 탐색기 → Windows와 함께 시작**을 켜면 다음 로그인부터 창 없이 트레이에서 실행됩니다. 자동 시작은 기본 꺼짐이며 현재 사용자에게만 적용됩니다. 앱을 다시 실행하면 기존 상주 프로세스의 창을 엽니다.
- 파일 하나를 우클릭해 **MoaView로 미리보기**를 선택할 수 있습니다. Windows 11에서는 **추가 옵션 표시** 안에 있습니다. 이 메뉴는 앱을 미리 켜 두지 않아도 사용할 수 있습니다.
- 앱의 **설정**에서 앱 전체의 테마를 **다크·라이트·세피아·오션·포레스트** 중 선택할 수 있습니다. 선택은 현재 사용자에 저장되어 다음 실행에도 유지됩니다. 마크다운 읽기 테마는 별도 설정이며 앱 테마와 독립적으로 유지됩니다.
- 이미지 탐색·확대·축소·화면 맞춤 도구는 아이콘 중심으로 표시됩니다. 아이콘 버튼은 키보드 탐색을 지원하고, 포인터 툴팁과 접근성 이름으로 동작을 설명합니다. 설정과 주요 작업 버튼은 이해하기 쉬운 텍스트를 함께 표시합니다.
- 현재 설치 파일은 코드 서명이 없는 개발 배포본입니다. Windows의 출처 확인 안내가 나타날 수 있습니다.

Windows 설정의 설치된 앱에서 MoaView를 제거하면 탐색기 우클릭 메뉴와 로그인 자동 시작 등록도 함께 제거됩니다. 개인 설정과 WebView2 캐시는 유지합니다. 이 데이터까지 지우려면 앱 종료 후 `%LOCALAPPDATA%\MoaView`를 삭제합니다. 다른 앱에서도 사용하는 WebView2 Runtime은 함께 제거하지 않습니다.

## 다시 빌드

Windows x64 빌드 환경과 .NET 8 SDK가 필요합니다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1 -Version 0.1.10
```

공식 Inno Setup 7.1.1을 `.tools`에 포터블 모드로 준비하고 서명을 확인합니다. WebView2 부트스트래퍼도 Microsoft 공식 배포처에서 내려받아 서명을 검증합니다. 앱을 자체 포함 방식으로 빌드한 뒤 설치 파일과 SHA-256을 `artifacts/installer`에 생성합니다. Inno Setup의 배포·사용 조건은 [공식 안내](https://jrsoftware.org/isinfo.php)를 참고하세요.

`-Version`과 `-RuntimeVersion`으로 버전을 명시할 수 있습니다. `-SkipPublish`는 이미 준비한 배포 폴더를 사용할 때만 지정하며 앱 버전과 포함 런타임을 확인합니다. 무인 설치(`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`)는 마지막 화면의 앱 실행 및 WebView2 설치를 생략하므로 WebView2가 필요하면 별도로 준비합니다.

## Windows 11 기본 우클릭 메뉴

Windows 11 기본 메뉴용 빌드는 `IExplorerCommand` DLL과 서명된 앱 식별 패키지를 함께 설치합니다. Windows가 패키지 등록을 완료하면 파일 하나를 우클릭해 **MoaView로 미리보기**를 바로 선택할 수 있습니다. 등록에 실패한 경우에는 **추가 옵션 표시**의 기존 항목으로 열 수 있습니다. [Microsoft 탐색기 통합 안내](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)

Visual Studio C++ x64 도구, Windows SDK, PowerShell 7이 있는 Windows 빌드 환경에서 다음과 같이 개발용 빌드를 만들 수 있습니다.

```powershell
pwsh -NoProfile -File scripts/Build-ExplorerIdentity.ps1 -Version 0.1.3 -DevelopmentCertificate
powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1 -Version 0.1.10 -ExplorerIdentityDirectory artifacts/explorer-identity/0.1.3
```

개발 빌드에서 생성한 공개 `MoaView-Development.cer`는 해당 빌드의 Windows 패키지 등록을 검증할 때 사용합니다. 설치하려는 Windows가 그 인증서를 신뢰해야 하며 설치 프로그램은 인증서 신뢰 저장소를 변경하지 않습니다. 신뢰된 배포용 서명 인증서가 `CurrentUser\My`에 있으면 첫 명령에서 `-DevelopmentCertificate` 대신 `-SigningThumbprint`로 인증서를 지정할 수 있습니다. [Microsoft 패키지 서명 안내](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps#build-and-sign-the-identity-package)
