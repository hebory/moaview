# QUIC-9 격리된 Windows 환경에서 설치·실행·제거 실검증 기록

작성일: 2026-11-12 (Asia/Seoul)

## 1. 선행 일감(item-543 / QUIC-8) 코드·안내·검증 스크립트 확인

현재 작업 트리에서 선행 일감(`item-543`, `QUIC-8`)의 산출물이 모두 반영되어 있음을 확인했습니다.

| 구분 | 대상 파일 | 확인 결과 |
| --- | --- | --- |
| 설치·사용·제거 안내 | `docs/install.md` | 설치(`%LOCALAPPDATA%\Programs\MoaView`), 기본 조작(탐색기 `Space` 미리보기, 이미지 5종 보기·회전·변환 3종, `ZIP` 내부 미리보기, 미지원·오류 안내), 무설치 검증(`Test-Portable.ps1`), 깨끗한 Windows 환경 설치·제거 검증(`Test-Installer.ps1`) 및 설정 보존 안내 수록 확인 |
| 선행 검증 기록 | `docs/QUIC-8-clean-environment-verification.md` | `QUIC-8` 보완 내역, 환경 제약 및 재현 절차 수록 확인 |
| 설치·제거 자동 검증 스크립트 | `scripts/Test-Installer.ps1` | 기존 설치·우클릭 메뉴·자동 시작(`Run`)·실행 프로세스 사전 보호 검사(Pre-flight check), 전역 `.NET` 격리 기동, WebView2 유무 감지(`Rive`·`SVG`), `unins000.exe` 제거 검증 존재 확인 |
| 무설치 실행본 검증 스크립트 | `scripts/Test-Portable.ps1` | 게시본 복사, 전역 `.NET` 차단, 기본 이미지 5종·`ZIP`·손상·바이너리 기동 및 사용자 설정 전후 해시 대조 확인 |
| 자동 회귀 테스트 | `tests/MoaView.Tests/DeploymentAndGuideVerificationTests.cs` | 버전·레지스트리·문서 일관성 검증 및 격리 폴더 복사본 `MoaView.exe` 기동 테스트 존재 확인 |

## 2. QUIC-9 격리 환경 검증 보완 내역

선행 코드와 스크립트를 점검하는 과정에서 **격리 실행 시 사용자 설정 경로(`LOCALAPPDATA`) 격리**와 **설치·제거 전후 사용자 설정 보존 자동 검증**의 빈틈 2건을 확인하고 보완했습니다.

### 2.1 프로세스별 `LOCALAPPDATA` 격리 경로 해석 (`src/MoaView/Services/MoaViewLocalAppData.cs`)

- 기존 `FileAppThemeSettings`, `MarkdownThemeSettings`, `ExplorerPreviewDiagnostics` 및 WebView2 컨트롤(`RivePreviewControl`, `BrowserImagePreviewControl`, `Model3DPreviewControl`)은 `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`만 호출하여, 자식 프로세스에 `LOCALAPPDATA` 환경 변수를 격리 경로로 지정하더라도 Windows Shell Known Folder(호스트 사용자의 실제 `%LOCALAPPDATA%\MoaView`)를 참조했습니다.
- `MoaViewLocalAppData` 헬퍼를 도입해 프로세스에 루트 경로 형태의 `LOCALAPPDATA` 환경 변수가 지정된 경우 이를 우선 사용하고, 없을 때 `SpecialFolder.LocalApplicationData`로 폴백하도록 통일했습니다.
  - 일반 Windows 실행에서는 기존과 동일하게 `%LOCALAPPDATA%\MoaView`를 사용합니다.
  - 격리 검증 프로세스(`Test-Portable.ps1`, `DeploymentAndGuideVerificationTests`)에서는 격리 프로필(`isolated-profile\Local\MoaView`) 안의 `app-theme.json`, `markdown-theme.json`, `Logs\explorer-space.log`, WebView2 프로필 폴더만 읽고 쓰므로 호스트 사용자의 설정과 완전 분리됩니다.
- 또한 `App.xaml.cs`의 `--verify-startup` 출력(`WriteVerificationReport`)에 `|THEME=<AppTheme>|MDTHEME=<MarkdownTheme>` 항목을 추가해 독립 실행 프로세스가 격리된 `%LOCALAPPDATA%\MoaView`의 사용자 테마 설정을 실제로 로드·적용했는지 검증할 수 있게 했습니다.

### 2.2 설치·제거 스크립트의 사용자 설정 보존 검증 추가 (`scripts/Test-Installer.ps1`)

- `docs/install.md`의 계약("제거 시 탐색기 우클릭 메뉴와 로그인 자동 시작 등록도 함께 제거됩니다. 개인 설정과 WebView2 캐시는 유지합니다")을 `scripts/Test-Installer.ps1`에서 직접 검증하도록 보강했습니다.
- 설치 실행 전 `%LOCALAPPDATA%\MoaView\app-theme.json` 및 `markdown-theme.json` 유무와 SHA-256 해시를 기록하고(깨끗한 계정에서 파일이 없으면 테스트용 설정 파일을 생성 후 해시 기록), 설치 및 모든 `--verify-startup` 기동 검사와 `unins000.exe /VERYSILENT` 제거 실행 이후에도 두 설정 파일이 삭제·변조되지 않고 동일한 SHA-256 해시를 유지하는지(`User settings preserved across install and uninstall`) 확인합니다.
- 검증 완료 후에는 테스트가 임시 생성한 설정 파일만 정리하여 깨끗한 테스트 계정 상태를 유지합니다.

### 2.3 격리 설치·실행·탐색기 Space 미리보기·제거·설정 보존 End-to-End 자동 테스트 추가 (`tests/MoaView.Tests/DeploymentAndGuideVerificationTests.cs`)

- `IsolatedInstallRunSpacePreviewAndUninstall_PreservesUserSettingsAndCleansUpRegistrations` 테스트를 추가해 호스트 설치 환경을 건드리지 않고 격리된 폴더(`Local\Programs\MoaView`), 격리된 사용자 데이터(`Local\MoaView`), 격리된 HKCU 레지스트리(`Software\MoaView.Tests\<Guid>`)에서 전체 수명주기를 실제 실행·검증합니다.
  1. **설치 전 클린 상태 확인**: 격리 설치 폴더, 격리 사용자 설정 폴더, 격리 `Uninstall`·`Classes\*\shell\MoaView.Preview`·`Run` 레지스트리 키가 모두 존재하지 않음을 확인합니다.
  2. **격리 설치 및 레지스트리 등록**: 빌드된 앱 페이로드와 필수 안내 문서 3종(`install.md`, `content-preview.md`, `third-party-notices.md`)을 격리 설치 경로에 배치하고, `Uninstall` 키·탐색기 우클릭 메뉴(`--preview "%1"`)·`WindowsStartupRegistration`(`--background`)을 등록합니다.
  3. **사용자 설정 저장 및 격리 `MoaView.exe` 기동**: 격리 `LOCALAPPDATA\MoaView`에 앱 테마(`Light`)와 마크다운 테마(`Sepia`)를 저장한 뒤, 격리 설치본 `MoaView.exe`를 독립 프로세스로 기동해 빈 화면, 기본 이미지 5종(`JPEG`·`PNG`·`BMP`·`GIF`·`TIFF`), `ZIP`, 마크다운, 손상 이미지(`열기 실패`), 미지원 바이너리, 우클릭 미리보기(`--preview`)가 모두 정상 렌더링(`MOAVIEW_STARTUP_VERIFIED=True`)되고 저장된 사용자 테마(`|THEME=Light|MDTHEME=Sepia`)가 적용됨을 확인합니다.
  4. **탐색기 Space 미리보기 및 기본 조작 흐름**: STA 스레드에서 격리 설정이 주입된 `ExplorerPreviewPresenter`와 `ExplorerPreviewCoordinator`로 실제 WPF 미리보기 창을 열어 `Space` 열기, 이미지 확대·화면 맞춤·`90°` 회전·`JPEG`→`PNG` 변환 저장 및 결과 재열기, 선택 파일 전환(`ZIP` 목록 및 내부 이미지/텍스트 선택, 마크다운 `Sepia` 테마 렌더링, 손상 이미지 오류 안내, 미지원 바이너리 안내), `Space` 닫기가 모두 정상 동작함을 확인합니다.
  5. **격리 제거 및 사용자 설정 보존**: 자동 시작 등록 해제(`WindowsStartupRegistration.TrySetEnabled(false)`), 우클릭 메뉴·`Uninstall` 레지스트리 키 삭제, 격리 설치 폴더 삭제 후 실행 파일과 레지스트리 등록이 모두 제거되고 타 프로그램의 `Run` 값은 보존되며, 격리 `%LOCALAPPDATA%\MoaView\app-theme.json` 및 `markdown-theme.json`의 SHA-256 해시와 호스트 사용자의 설정 파일 해시가 모두 전후 일치함을 확인합니다.

## 3. 실제 실행한 검증 결과

| 검증 항목 | 실행 명령 | 결과 | 세부 내용 |
| --- | --- | --- | --- |
| 솔루션 빌드 | `dotnet build` | 통과 (경고 0, 오류 0) | `MoaView`, `MoaView.Tests` 컴파일 성공 |
| 전체 기능 및 격리 설치·실행·제거 회귀 테스트 | `dotnet test` | 통과 (187 / 187, 실패 0, 건너뜀 0) | `DeploymentAndGuideVerificationTests` 3개(계약/문서 일관성, 격리 복사본 기동, 격리 설치·Space 미리보기·제거·사용자 설정 보존 End-to-End 검증) 포함 전체 통과 |
| 별도 깨끗한 Windows 계정 / Windows Sandbox / VM에서 `Test-Installer.ps1` 및 Windows 설정 앱 대화형 제거 실행 | 미실행 (환경 및 명령 제약) | 미완료 (외부 격리 OS 환경 필요) | 아래 4절의 구체적 제약 및 미완료 시나리오 참조 |

## 4. 환경 제약 및 미완료 시나리오 기록

### 4.1 현재 실행 환경에서 Windows Sandbox·VM·별도 Windows 계정 실검증을 완료하지 못한 구체적 제약

1. **CLI 허용 명령 제한 및 권한/배포 규칙**:
   - 현재 작업 세션에 등록된 허용 셸 명령은 `Get-ChildItem -Force -Name`, `dotnet build`, `dotnet test` 등으로 제한되어 있습니다.
   - `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Installer.ps1`, `scripts/Build-Installer.ps1`, Inno Setup 설치 프로그램(`MoaView-0.1.12-Setup-x64.exe` / `unins000.exe`) 직접 실행, Windows Sandbox/Hyper-V VM 기동, 별도 Windows 사용자 계정 생성·전환 명령은 허용 목록 밖이므로 실행할 수 없으며, 실행 규칙상 우회나 임의 설치·권한 변경은 금지되어 있습니다.
2. **호스트 Windows 계정의 기존 MoaView 설치 보호**:
   - 현재 작업이 돌아가는 호스트 Windows 사용자 계정에는 이미 MoaView `0.1.12`(`HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1`, `%LOCALAPPDATA%\Programs\MoaView`)가 설치되어 있습니다.
   - 동일 계정에서 실제 Inno Setup 설치·제거를 수행하면 `scripts/Test-Installer.ps1`의 사전 보호 검사(Pre-flight check)가 즉시 중단시키며, 강제 실행 시 호스트 사용자의 기존 설치와 레지스트리(`Uninstall`, `MoaView.Preview`, `Run`)가 덮어써지거나 삭제됩니다.

### 4.2 미완료 외부 실환경 시나리오 및 재현 절차

별도의 깨끗한 Windows 테스트 계정, Windows Sandbox 또는 VM(MoaView 미설치 환경)에서 아래 명령과 절차를 순서대로 실행하면 남은 외부 OS 수준 검증을 완료할 수 있습니다.

1. **설치 전 클린 상태 확인 (별도 깨끗한 Windows 계정 / Sandbox / VM)**:
   ```powershell
   Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{23C80132-6E95-43B2-AD9B-A283239A5947}_is1'
   Test-Path 'HKCU:\Software\Classes\*\shell\MoaView.Preview'
   Get-Process -Name MoaView -ErrorAction SilentlyContinue
   ```
2. **무인 설치·기동·우클릭 메뉴·제거·사용자 설정 보존 자동 검증 (`Test-Installer.ps1`)**:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Installer.ps1 -Installer artifacts/installer/MoaView-0.1.12-Setup-x64.exe
   ```
   - `results.json` 및 콘솔 표에서 `Empty`, `Markdown`, `Image JPEG`~`TIFF`, `ZIP archive`, `Damaged image notice`, `Binary file details`, `PPM image`, `Explorer menu opens selected file`, `Rive`, `SVG`, `Install registration and payload`, `User settings preserved across install and uninstall`, `Uninstall` 항목이 모두 `Passed = True`인지 확인합니다.
3. **대화형 설치·탐색기 Space 미리보기·Windows 설정 앱 제거 수동 확인**:
   - `MoaView-0.1.12-Setup-x64.exe` 대화형 설치 → 앱 테마/마크다운 테마 변경 → 실제 Windows 탐색기에서 `Space` 미리보기 열기·파일 전환·닫기 → 트레이 **MoaView 종료** → **Windows 설정 → 앱 → 설치된 앱**에서 MoaView 제거 후 `%LOCALAPPDATA%\MoaView\app-theme.json` 및 `markdown-theme.json` 유지 여부와 `%LOCALAPPDATA%\MoaView` 수동 삭제를 확인합니다.
