# QUIC-8 깨끗한 Windows 환경 설치·실행·제거 안내 및 검증 기록

## 1. 목적 및 배경

완료 조건 **C8**(실제 기능 회귀 검증)과 이미지·ZIP 미리보기 검증 통과 이후 남아 있는 **C9**(`실행·설치·사용 안내`: 깨끗한 Windows 환경에서 안내에 따라 설치 또는 무설치 실행을 완료하고, 실행·제거 방법과 기본 조작, 형식 지원 범위를 따라 할 수 있는지 확인한다)를 점검하고, 안내 문서·검증 스크립트·자동 회귀 테스트를 보완한 기록입니다.

## 2. 점검 및 보완 내역

### 2.1 설치·무설치 실행·기본 조작·형식 범위·제거 안내 보완 (`docs/install.md`)

- **기본 조작과 형식 지원 범위 확인 절차 추가**:
  - 실행 및 트레이 완전 종료(**MoaView 종료**), 탐색기 `Space` 미리보기 열기·전환·닫기 및 우클릭 **MoaView로 미리보기**(`MoaView.exe --preview "<파일>"`) 확인 방법을 명시했습니다.
  - 기본 지원 이미지 5종(`JPEG`, `PNG`, `BMP`, `GIF`, `TIFF`) 보기·확대·축소·원본 크기·화면 맞춤·`90°` 회전·이전/다음 이동과 `JPEG`·`PNG`·`BMP` 간 변환 저장(원본 덮어쓰기 차단 및 `GIF`·`TIFF` 사전 미지원 안내) 흐름을 정리했습니다.
  - `ZIP` 내부 이미지·텍스트 미리보기, 암호화·손상 아카이브 및 손상 이미지 오류 안내, 미지원 바이너리 파일 정보 표시, WebView2 미설치 환경의 런타임 안내 동작을 한 문서에서 따라 할 수 있도록 보강했습니다.
- **깨끗한 Windows 환경 설치·실행·제거 검증 절차 추가**:
  - 별도 Windows 테스트 계정·Windows Sandbox·VM에서 설치 전 클린 상태를 확인하는 명령, `scripts/Test-Portable.ps1` 및 `scripts/Test-Installer.ps1` 자동 검증 절차, 대화형 설치·탐색기 Space 미리보기·Windows 설정 앱 제거 및 `%LOCALAPPDATA%\MoaView` 잔여 데이터 삭제 절차를 단계별로 문서화했습니다.

### 2.2 설치·제거 자동 검증 스크립트 보완 (`scripts/Test-Installer.ps1`)

1. **기존 사용자 환경 보호(Pre-flight check) 강화**:
   - 기존 `Uninstall` 레지스트리 키(`{23C80132-6E95-43B2-AD9B-A283239A5947}_is1`), 탐색기 우클릭 메뉴 키(`HKCU:\Software\Classes\*\shell\MoaView.Preview`), `MoaView.ExplorerIntegration` 패키지뿐 아니라 `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`의 `MoaView` 자동 시작 값과 실행 중인 `MoaView` 프로세스 유무를 함께 검사합니다.
   - 기존에 실행 중이거나 자동 시작이 등록된 계정에서 스크립트를 실행해 프로세스가 종료(`CloseApplications=yes`)되거나 자동 시작 등록이 제거(`uninsdeletevalue`)되는 부작용을 예방합니다.
2. **전역 .NET 런타임 차단 및 포함 문서·핵심 형식 기동 검증 확대**:
   - 자식 프로세스의 `DOTNET_ROOT`, `DOTNET_ROOT_X64`, `DOTNET_MULTILEVEL_LOOKUP=0`, `DOTNET_ROLL_FORWARD=Disable`, `DOTNET_SHARED_STORE`를 격리합니다.
   - 설치된 폴더에 실행·런타임·라이선스 파일과 함께 안내 문서(`install.md`, `content-preview.md`, `third-party-notices.md`)가 포함되었는지 확인합니다.
   - 빈 창(`Empty`), 마크다운(`TextLoaded`), 기본 이미지 5종(`JPEG`·`PNG`·`BMP`·`GIF`·`TIFF` → `ImageLoaded`), `ZIP` 아카이브(`ArchiveLoaded`), 손상된 JPEG(`Error` 및 `BADGE=열기 실패`), 미지원 바이너리(`TextLoaded`), `PPM` 이미지를 모두 설치본 `MoaView.exe --verify-startup`으로 검증합니다.
3. **WebView2 Runtime 미설치 깨끗한 Windows 환경 대응**:
   - 무인 설치(`/VERYSILENT`)는 `installer/MoaView.iss`의 `skipifsilent` 설정에 따라 WebView2 부트스트래퍼 실행을 생략합니다.
   - `Test-Installer.ps1`가 EdgeUpdate 레지스트리(`{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`)로 WebView2 Runtime 설치 여부를 감지하여, 런타임이 설치된 환경에서는 `Rive`(`|RIVE=ready|`)와 `SVG`(`|SPECIALIZED=ready|`) 렌더링 완료를 확인하고, 런타임이 없는 깨끗한 Windows 환경에서는 앱이 비정상 종료되지 않고 안내 상태(`|RIVE=error|`, `|SPECIALIZED=error|`)를 표시하는지 검증합니다.
4. **제거(Uninstall) 정리 검증 강화**:
   - `unins000.exe /VERYSILENT` 실행 후 `MoaView.exe`, `Uninstall` 레지스트리 키, 탐색기 우클릭 메뉴 키, `MoaView.ExplorerIntegration` 패키지, `HKCU:\Software\Microsoft\Windows\CurrentVersion\Run`의 `MoaView` 값이 모두 제거되었는지 확인합니다.

### 2.3 배포 계약·문서 일관성 및 격리 복사본 실행 자동 회귀 테스트 추가 (`tests/MoaView.Tests/DeploymentAndGuideVerificationTests.cs`)

- **`InstallerScriptsAndGuides_StayConsistentWithProjectVersionAndRegistrationContracts`**:
  - `src/MoaView/MoaView.csproj`, `installer/MoaView.iss`, `scripts/Build-Installer.ps1`, `scripts/Test-Installer.ps1`, `scripts/Test-Portable.ps1`, `docs/install.md`, `docs/content-preview.md`, `README.md`, `SupportedFileFormats` 간 앱 버전(`0.1.12`), 자체 포함 런타임 버전(`8.0.33`), 레지스트리 경로 및 `--preview`·`--background` 명령 인수, 필수 포함 문서, 기본 이미지 5종·변환 3종·ZIP 지원 안내가 일치하는지 `dotnet test`에서 상시 검증합니다.
- **`IsolatedApplicationCopy_VerifiesStartupAndShellPreviewWithoutTouchingUserSettings`**:
  - 빌드된 애플리케이션 폴더 전체를 별도 임시 디렉터리에 복사하고 `LOCALAPPDATA`, `APPDATA`, `USERPROFILE`, `TEMP`, `TMP`를 격리한 상태에서 독립 프로세스로 `MoaView.exe --verify-startup`(빈 창, `JPEG`·`PNG`·`BMP`·`GIF`·`TIFF`, `ZIP`, 마크다운, 손상 이미지 `열기 실패`, 미지원 바이너리)과 탐색기 우클릭 미리보기 명령(`MoaView.exe --verify-startup --preview "<파일>"`)을 실행합니다.
  - 모든 케이스가 정상 렌더링 후 종료 코드 `0`으로 끝나고, 호스트 사용자의 `%LOCALAPPDATA%\MoaView\app-theme.json` 및 `markdown-theme.json` 해시가 전후로 변경되지 않음을 검증합니다.

## 3. 현재 세션 검증 결과 및 환경 제약

| 구분 | 검증 방법 | 결과 | 비고 |
| --- | --- | --- | --- |
| 솔루션 빌드 | `dotnet build` | 통과 (경고 0, 오류 0) | `MoaView`, `MoaView.Tests` 컴파일 확인 |
| 기능 및 격리 기동 회귀 테스트 | `dotnet test` | 통과 (186 / 186) | 격리 폴더 복사본 `MoaView.exe` 10개 기동 케이스 + `--preview` 모드 + 배포/문서 계약 검증 포함 |
| 무설치 게시본 기동 (`Test-Portable.ps1`) | QUIC-7 기록 확인 | 통과 (9 / 9) | [QUIC-7 검증 기록](QUIC-7-usability-verification.md) 참조 |
| 깨끗한 별도 Windows 환경에서 실제 설치·제거 (`Test-Installer.ps1`) | 현재 세션 직접 실행 불가 | 미실행 (외부 환경 필요) | 아래 환경 및 권한 제약 참조 |

### 깨끗한 별도 Windows 환경을 현재 세션에서 직접 실행하지 못한 구체적 이유

1. **호스트 Windows 계정에 기존 MoaView 0.1.12 설치 존재**:
   - 현재 작업이 실행되는 Windows 사용자 계정에는 이미 `HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1` 및 `%LOCALAPPDATA%\Programs\MoaView`에 MoaView `0.1.12`가 설치되어 있습니다.
   - 같은 계정에서 강제로 설치·제거를 수행하면 사용자의 기존 설치 파일과 레지스트리 등록이 덮어써지거나 삭제됩니다.
2. **격리된 깨끗한 Windows 환경(별도 테스트 계정·Windows Sandbox·VM) 접근 불가 및 CLI 명령 제한**:
   - 현재 CLI 세션에서는 다른 Windows 사용자 계정으로 전환하거나 Windows Sandbox/VM을 기동할 수 없습니다.
   - 또한 작업 규칙상 허용된 빌드·테스트 명령(`dotnet build`, `dotnet test`) 밖의 임의 설치·배포 명령이나 `powershell`/`dotnet publish`/설치 프로그램 실행이 허용되지 않습니다.

## 4. 깨끗한 Windows 환경에서 C9를 완료하기 위한 재현 절차

MoaView가 설치된 적 없는 별도의 Windows 10(1809 이상) 또는 Windows 11 테스트 계정·Windows Sandbox·VM에서 아래 순서로 실행하면 C9 검증을 완료할 수 있습니다.

1. **설치 파일 빌드 (개발/빌드 PC에서 준비 시)**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1 -Version 0.1.12
   ```
2. **무설치 실행본 자동 검증 (`Test-Portable.ps1`)**:
   ```powershell
   powershell.exe -NoProfile -File scripts/Test-Portable.ps1 -PublishDirectory artifacts/publish/win-x64
   ```
   - 출력 JSON의 `"status": "passed"` 및 `"settingsUnchanged": true`를 확인합니다.
3. **깨끗한 Windows 계정/VM에서 설치·실행·우클릭 메뉴·제거 자동 검증 (`Test-Installer.ps1`)**:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Installer.ps1 -Installer artifacts/installer/MoaView-0.1.12-Setup-x64.exe
   ```
   - 출력 표의 모든 항목(`Empty`, `Markdown`, `Image JPEG`~`TIFF`, `ZIP archive`, `Damaged image notice`, `Binary file details`, `PPM image`, `Explorer menu opens selected file`, `Rive`, `SVG`, `Install registration and payload`, `Uninstall`)이 `Passed = True`인지와 `artifacts/install-check/<실행ID>/results.json`을 확인합니다.
4. **대화형 설치·탐색기 Space 미리보기·Windows 설정 제거 수동 확인**:
   - [docs/install.md](install.md)의 **기본 조작과 형식 지원 범위 확인** 및 **깨끗한 Windows 환경 설치·실행·제거 검증** 3단계를 따라 설치, 탐색기 `Space` 미리보기 열기/전환/닫기, 이미지 회전·변환 저장, ZIP 내부 보기, 트레이 종료, Windows 설정 앱 제거를 확인합니다.
