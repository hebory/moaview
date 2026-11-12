# MoaView Windows 설치

`artifacts/installer/MoaView-0.1.12-Setup-x64.exe`를 실행해 설치합니다. Windows 10 1809 이상 또는 Windows 11의 x64/호환 환경용입니다. 관리자 권한 없이 현재 사용자에게 설치됩니다.

- 기본 경로: `%LOCALAPPDATA%\Programs\MoaView`
- 시작 메뉴에 MoaView가 등록됩니다. 바탕 화면 바로가기는 설치 중 선택합니다.
- .NET 8.0.33 Windows Desktop 런타임을 포함하므로 .NET을 따로 설치하지 않습니다.
- Rive·3D 모델과 SVG·WebP·AVIF·ICO·APNG·BPG·GIF 애니메이션 미리보기에는 WebView2 Runtime이 필요합니다. 설치되지 않았다면 마지막 화면에서 Microsoft WebView2 설치를 선택할 수 있으며 이 단계에는 인터넷이 필요합니다.
- 설치 완료 화면에서 MoaView 실행이 기본 선택됩니다. 기본 창을 닫아도 트레이에서 계속 실행되므로 탐색기에서 파일 하나를 선택하고 Space로 미리볼 수 있습니다. 트레이 아이콘 더블클릭으로 창을 열고, 우클릭 메뉴에서 Space 전환과 **MoaView 종료**를 선택합니다.
- 0.1.12부터 Space 입력 훅을 5초마다 새로 등록합니다. Windows가 지연된 키보드 훅을 알림 없이 제거해도 앱을 재시작하지 않고 복구하며, 훅 등록 실패 시 다시 시도합니다. 복구 기록은 `%LOCALAPPDATA%\MoaView\Logs\explorer-space.log`에 최대 약 128KB씩 두 파일로 보관합니다. 파일 경로나 타이핑 내용은 기록하지 않습니다.
- 설치할 때 **Windows 로그인 시 MoaView 자동 실행**을 선택하거나, 앱의 **설정 → 탐색기 → Windows와 함께 시작**을 켜면 다음 로그인부터 창 없이 트레이에서 실행됩니다. 자동 시작은 기본 꺼짐이며 현재 사용자에게만 적용됩니다. 앱을 다시 실행하면 기존 상주 프로세스의 창을 엽니다.
- 파일 하나를 우클릭해 **MoaView로 미리보기**를 선택할 수 있습니다. Windows 11에서는 **추가 옵션 표시** 안에 있습니다. 이 메뉴는 앱을 미리 켜 두지 않아도 사용할 수 있습니다.
- 앱의 **설정**에서 앱 전체의 테마를 **다크·라이트·세피아·오션·포레스트** 중 선택할 수 있습니다. 선택은 현재 사용자에 저장되어 다음 실행에도 유지됩니다. 마크다운 읽기 테마는 별도 설정이며 앱 테마와 독립적으로 유지됩니다.
- 이미지 탐색·확대·축소·화면 맞춤 도구는 아이콘 중심으로 표시됩니다. 아이콘 버튼은 키보드 탐색을 지원하고, 포인터 툴팁과 접근성 이름으로 동작을 설명합니다. 설정과 주요 작업 버튼은 이해하기 쉬운 텍스트를 함께 표시합니다.
- 현재 설치 파일은 코드 서명이 없는 개발 배포본입니다. Windows의 출처 확인 안내가 나타날 수 있습니다.

Windows 설정의 설치된 앱에서 MoaView를 제거하면 탐색기 우클릭 메뉴와 로그인 자동 시작 등록도 함께 제거됩니다. 개인 설정과 WebView2 캐시는 유지합니다. 이 데이터까지 지우려면 앱 종료 후 `%LOCALAPPDATA%\MoaView`를 삭제합니다. 다른 앱에서도 사용하는 WebView2 Runtime은 함께 제거하지 않습니다.

## 다시 빌드

Windows x64 빌드 환경과 .NET 8 SDK가 필요합니다.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1 -Version 0.1.12
```

공식 Inno Setup 7.1.1을 `.tools`에 포터블 모드로 준비하고 서명을 확인합니다. WebView2 부트스트래퍼도 Microsoft 공식 배포처에서 내려받아 서명을 검증합니다. 앱을 자체 포함 방식으로 빌드한 뒤 설치 파일과 SHA-256을 `artifacts/installer`에 생성합니다. Inno Setup의 배포·사용 조건은 [공식 안내](https://jrsoftware.org/isinfo.php)를 참고하세요.

`-Version`과 `-RuntimeVersion`으로 버전을 명시할 수 있습니다. `-SkipPublish`는 이미 준비한 배포 폴더를 사용할 때만 지정하며 앱 버전과 포함 런타임을 확인합니다. 무인 설치(`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`)는 마지막 화면의 앱 실행 및 WebView2 설치를 생략하므로 WebView2가 필요하면 별도로 준비합니다.

## Windows 11 기본 우클릭 메뉴

Windows 11 기본 메뉴용 빌드는 `IExplorerCommand` DLL과 서명된 앱 식별 패키지를 함께 설치합니다. Windows가 패키지 등록을 완료하면 파일 하나를 우클릭해 **MoaView로 미리보기**를 바로 선택할 수 있습니다. 등록에 실패한 경우에는 **추가 옵션 표시**의 기존 항목으로 열 수 있습니다. [Microsoft 탐색기 통합 안내](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)

Visual Studio C++ x64 도구, Windows SDK, PowerShell 7이 있는 Windows 빌드 환경에서 다음과 같이 개발용 빌드를 만들 수 있습니다.

```powershell
pwsh -NoProfile -File scripts/Build-ExplorerIdentity.ps1 -Version 0.1.3 -DevelopmentCertificate
powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1 -Version 0.1.12 -ExplorerIdentityDirectory artifacts/explorer-identity/0.1.3
```

개발 빌드에서 생성한 공개 `MoaView-Development.cer`는 해당 빌드의 Windows 패키지 등록을 검증할 때 사용합니다. 설치하려는 Windows가 그 인증서를 신뢰해야 하며 설치 프로그램은 인증서 신뢰 저장소를 변경하지 않습니다. 신뢰된 배포용 서명 인증서가 `CurrentUser\My`에 있으면 첫 명령에서 `-DevelopmentCertificate` 대신 `-SigningThumbprint`로 인증서를 지정할 수 있습니다. [Microsoft 패키지 서명 안내](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps#build-and-sign-the-identity-package)


## 기본 조작과 형식 지원 범위 확인

설치본(`%LOCALAPPDATA%\Programs\MoaView\MoaView.exe`)과 무설치 실행본(`artifacts/publish/win-x64/MoaView.exe`)은 동일한 기본 조작과 형식 지원 범위를 제공합니다.

- **실행과 완전 종료**: `MoaView.exe`를 실행하면 기본 창과 시스템 트레이 아이콘이 함께 시작됩니다. 기본 창을 닫아도 트레이에서 상주하므로 완전히 끝내려면 트레이 아이콘 우클릭 메뉴에서 **MoaView 종료**를 선택합니다.
- **탐색기 Space 미리보기 및 우클릭 메뉴 (C2)**:
  - 트레이에 MoaView가 실행 중일 때 Windows 탐색기에서 파일 하나를 선택하고 `Space`를 누르면 미리보기 창이 열리고, 다시 `Space` 또는 `Esc`를 누르면 닫힙니다. 같은 탐색기 창에서 다른 파일을 선택하면 미리보기 내용이 즉시 바뀝니다.
  - 상주 실행 중이 아니어도 파일을 우클릭해 **MoaView로 미리보기**(Windows 11에서 패키지 미등록 시 **추가 옵션 표시** 안)를 선택하면 단일 미리보기 창(`MoaView.exe --preview "<파일>"`)이 열립니다. [탐색기 연동 상세](explorer-preview.md)
- **이미지 보기·조작·변환 (C3 · C4 · C5)**:
  - 기본 지원 이미지(`JPEG`, `PNG`, `BMP`, `GIF`, `TIFF`)를 열면 확대·축소(`-` / `+`, `Ctrl + 마우스 휠`), 원본 크기(`0` / `/`), 화면 맞춤(`F` / `*`), `90°` 회전(`L` / `R`), 같은 폴더의 이전·다음 이미지 이동(`←` / `→`, `PageUp` / `PageDown`)을 사용할 수 있습니다.
  - 이미지 도구의 **변환 저장…**(`Ctrl+Shift+S`)은 `JPEG`·`PNG`·`BMP` 간 조합만 변환하며 원본 파일 덮어쓰기를 차단합니다. 변환이 끝나면 완료 배너의 **결과 파일 열기**로 저장된 파일을 바로 확인합니다. `GIF`·`TIFF` 등 변환 미지원 형식은 저장 대화상자를 열기 전에 미지원 사유를 안내합니다.
- **압축파일 내부 미리보기 및 미지원·오류 안내 (C6 · C7)**:
  - `ZIP`(및 `RAR`·`7Z`·`TAR`·`CBZ` 등) 파일을 열면 왼쪽 목록에서 내부 이미지·일반 텍스트·마크다운·3D 모델을 선택해 임시 파일 추출 없이 미리봅니다.
  - 암호화·손상된 압축파일이나 손상된 이미지 파일은 앱이 멈추거나 종료되지 않고 원인을 안내합니다. 전용 보기가 없는 일반 텍스트는 내용을 표시하고, 미지원 바이너리 파일은 크기·수정 시각·파일 앞부분 16진수를 표시합니다.
  - WebView2가 필요한 형식(`SVG`, `WebP`, `AVIF`, `ICO`, `APNG`, `BPG`, 애니메이션 `GIF`, `Rive`, `3D 모델`)은 WebView2 Runtime이 없는 환경에서 비정상 종료 없이 런타임 필요 안내를 표시합니다. [형식별 지원 범위와 제한](content-preview.md)

## 무설치 실행본 검증

자체 포함 게시 폴더의 `MoaView.exe`를 실행할 수 있으며 .NET 설치가 필요하지 않습니다. 폴더 전체를 함께 유지하세요. 기존 설치 앱이 트레이에서 실행 중이면 먼저 트레이 메뉴의 **MoaView 종료**로 끝내야 새 실행본이 사용됩니다.

```powershell
powershell.exe -NoProfile -File scripts/Test-Portable.ps1 -PublishDirectory artifacts/publish/win-x64
```

이 검사는 새 폴더 복사본에서 실제 창을 기동해 JPEG·PNG·BMP·정적 GIF·TIFF, ZIP 목록, 손상·미지원 안내를 확인하고 결과 JSON을 남깁니다. 전역 .NET 경로는 자식 프로세스에 한해 제한하고, 기존 설치·레지스트리를 변경하지 않으며 사용자 테마 설정 해시도 대조합니다. 깨끗한 Windows 설치·제거 또는 WebView2 미설치 환경 검증을 대신하지 않습니다. [QUIC-7 검증 범위](QUIC-7-usability-verification.md) · [QUIC-8 검증 및 한계 기록](QUIC-8-clean-environment-verification.md) · [QUIC-9 격리 환경 실검증 기록](QUIC-9-isolated-environment-verification.md) · [QUIC-10 잔여 시나리오 검증 기록](QUIC-10-residual-scenario-verification.md)

## 깨끗한 Windows 환경 설치·실행·제거 검증

설치·제거 검증 스크립트(`scripts/Test-Installer.ps1`)는 현재 사용자의 설치 레지스트리(`HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1`), 탐색기 우클릭 메뉴(`HKCU:\Software\Classes\*\shell\MoaView.Preview`), 로그인 자동 시작(`HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`의 `MoaView`), Windows 11 패키지(`MoaView.ExplorerIntegration`)를 실제로 등록하고 제거(`unins000.exe`)합니다. 기존 사용자 환경을 보호하기 위해 MoaView가 이미 설치되어 있거나 자동 시작이 등록되어 있거나 `MoaView` 프로세스가 실행 중이면 검사를 즉시 중단하므로, 반드시 **별도의 깨끗한 Windows 테스트 계정, Windows Sandbox 또는 가상 머신(VM)**에서 실행합니다.

1. **설치 전 깨끗한 상태 확인**:
   ```powershell
   Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1'
   Test-Path 'HKCU:\Software\Classes\*\shell\MoaView.Preview'
   Get-Process -Name MoaView -ErrorAction SilentlyContinue
   ```
   세 항목 모두 결과가 없어야 합니다.
2. **설치·기동·우클릭 메뉴·제거 자동 검증 실행**:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Installer.ps1 -Installer artifacts/installer/MoaView-0.1.12-Setup-x64.exe
   ```
   - `artifacts/install-check/<실행ID>/MoaView Test` 경로에 무인 설치(`/VERYSILENT`)하고 필수 런타임·에셋·안내 문서(`install.md`, `content-preview.md`, `third-party-notices.md`) 포함 여부를 확인합니다.
   - 전역 `.NET` 탐색을 차단한 상태로 설치된 `MoaView.exe --verify-startup`을 실행해 빈 시작 화면, 마크다운, 기본 이미지 5종(`JPEG`·`PNG`·`BMP`·`GIF`·`TIFF`), `ZIP` 아카이브, 손상된 이미지 오류 안내(`열기 실패`), 미지원 바이너리 상세 안내, `PPM` 이미지를 검증합니다.
   - 탐색기 우클릭 메뉴 동사(`MoaView.Preview` 또는 Windows 11 `IExplorerCommand`)로 실제 파일 창이 열리고 닫히는지 확인합니다.
   - WebView2 Runtime 설치 여부를 자동 감지하여, 설치된 환경에서는 `Rive`·`SVG` 렌더링 준비(`ready`)를 확인하고, WebView2가 없는 깨끗한 환경에서는 비정상 종료 없이 안내 상태(`error`)로 표시되는지 확인합니다.
   - 마지막 `finally` 단계에서 `unins000.exe /VERYSILENT`를 실행해 실행 파일, `Uninstall` 레지스트리 키, 탐색기 우클릭 메뉴 키, `Run` 자동 시작 값, Windows 11 식별 패키지가 모두 제거되고 `%LOCALAPPDATA%\MoaView\app-theme.json` 및 `markdown-theme.json` 사용자 설정 해시가 보존되었는지(`User settings preserved across install and uninstall`) 검증한 뒤 `artifacts/install-check/<실행ID>/results.json`에 결과를 저장합니다.
3. **대화형 설치·탐색기 Space 미리보기·완전 제거 수동 확인**:
   - `MoaView-0.1.12-Setup-x64.exe`를 실행해 기본 경로(`%LOCALAPPDATA%\Programs\MoaView`)에 설치하고 앱을 실행합니다.
   - 탐색기에서 지원 이미지(`JPEG`·`PNG`·`BMP`·`GIF`·`TIFF`), `ZIP` 파일, 미지원 파일을 차례로 선택해 `Space`로 미리보기 열기·파일 전환·`Space`/`Esc` 닫기와 이미지 회전·확대·변환 저장을 확인합니다.
   - 트레이 메뉴에서 **MoaView 종료**를 선택한 뒤 **Windows 설정 → 앱 → 설치된 앱**에서 MoaView를 제거합니다.
   - 개인 테마 설정과 WebView2 캐시까지 모두 정리하려면 `%LOCALAPPDATA%\MoaView` 폴더를 삭제합니다.
