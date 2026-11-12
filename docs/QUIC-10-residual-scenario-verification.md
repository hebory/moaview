# QUIC-10 잔여 실환경 검증 시나리오의 접근 가능성 조사 및 격리 실검증 기록

작성일: 2026-11-12 (Asia/Seoul)

## 1. 목적 및 선행 검증(QUIC-7~9) 대조

선행 검증 문서(`docs/QUIC-7-usability-verification.md`, `docs/QUIC-8-clean-environment-verification.md`, `docs/QUIC-9-isolated-environment-verification.md`)와 실행·사용 안내(`docs/explorer-preview.md`, `docs/install.md`, `README.md`)를 대조해, 최신 독립 검증에서 미완료로 남은 **C2 탐색기 잔여 시나리오 3건**(`탐색기 탭 전환`, `탐색기 창 종료·재시작`, `Space 길게 누르기`)과 **C9 잔여 시나리오 1건**(`별도 깨끗한 Windows 환경 설치·실행·제거`)의 검증 조건과 접근 가능성을 정리했습니다.

기존 문서와의 중복을 피하기 위해 이번 문서에서는 **(1) 허가된 격리 Windows 환경에서 직접 검증 가능한 항목과 실제 확인 결과**, **(2) 탐색기 창 종료·재시작 시 발견된 상태 정리 보완 내역**, **(3) 접근 불가 항목의 실행 환경·규칙 근거 및 외부 재현 경계**만 구분해 기록합니다.

## 2. 잔여 시나리오별 접근 가능성 및 안전한 검증 조건

| 구분 | 잔여 시나리오 | 허가된 격리 환경에서 직접 검증 가능한 범위 (실제 확인 완료) | 현재 세션에서 접근 불가한 범위 및 제약 근거 |
| --- | --- | --- | --- |
| **C2-1** | Windows 11 탐색기 탭 전환 | 동일 최상위 탐색기 `WindowHandle` 내 서로 다른 탭(`SHELLDLL_DefView` `ViewHandle`) 전환 시 (a) 200ms 주기 갱신 전 즉시 `Space` 입력으로 새 탭 선택 열기, (b) 주기 갱신(`RefreshAsync`)으로 열린 WPF 미리보기 창을 닫지 않고 탭별 파일(이미지·ZIP)로 전환 렌더링, (c) 동일 파일 경로가 선택된 다른 탭으로 전환 후 `Space` 닫기, (d) 빈 탭 전환 시 자동 닫기 | 호스트 데스크톱의 실제 `explorer.exe` 창에 포커스를 강제로 옮겨 탭 단축키(`Ctrl+T` / `Ctrl+Tab`)를 전역 주입하는 조작은 실행 중인 사용자 작업 및 기존 상주 MoaView와 충돌하므로 수행하지 않음 |
| **C2-2** | 탐색기 창 종료·재시작 | 소유 탐색기 HWND(`explorerWindow1`)에서 미리보기를 연 뒤 창을 닫아(`IsWindow == false`) 포커스가 탐색기 밖(`null`)으로 이동했을 때 이전 미리보기 창과 `ExplorerPreviewCoordinator` 상태가 정리되고, 새 탐색기 HWND(`explorerWindow2`) 기동 후 `Space`로 새 소유 창 연결(`Owner == hwnd2`) 및 렌더링이 재개되는지 실제 Win32 HWND + WPF 창으로 확인 | 실제 Windows 셸 프로세스(`explorer.exe`)를 `Stop-Process` / `taskkill`로 강제 종료·재시작하는 행위는 허용 명령 목록 밖이며 호스트 작업 표시줄·트레이 세션을 중단시키므로 금지 |
| **C2-3** | `Space` 길게 누르기 | (a) `SpaceKeyGesture`에서 첫 `WM_KEYDOWN`만 `Toggle`되고 연속 키 반복(`WM_KEYDOWN`)은 횟수와 무관하게 `Consume`되는지, (b) 물리적 키 눌림 유지 중 또는 최근 반복 입력(`1500ms` 미만) 동안 유지보수 타이머(`RecoverReleasedKey`)가 눌림 상태를 해제하지 않고 키-업 유실 후 `1500ms` 경과 시에만 복구하는지, (c) 실제 Win32 `WH_KEYBOARD_LL` 훅(`ExplorerSpaceKeyHook`)의 주기적 재등록(`RegistrationCount >= 2`) 중 길게 누르기 열기·닫기가 실제 WPF 미리보기 창을 각각 1회만 열고 닫는지 확인 | 물리 키보드 하드웨어 장시간 누름 또는 전역 `SendInput` 주입은 호스트 포그라운드 앱에 공백 입력을 발생시키므로 격리 테스트 스레드에서만 수행 |
| **C9** | 별도 깨끗한 Windows 환경 설치·실행·제거 | 격리 설치 경로(`Local\Programs\MoaView`), 격리 `LOCALAPPDATA\MoaView`, 격리 HKCU 레지스트리(`Software\MoaView.Tests\<Guid>`)에서 설치 페이로드·문서 3종 배치, `Uninstall`·우클릭 메뉴(`--preview "%1"`)·`Run`(`--background`) 등록, 전 형식(`Empty`·5종 이미지·`PPM`·`ZIP`·마크다운·손상·바이너리·`Rive`·`SVG`·레지스트리 우클릭 명령 템플릿) 독립 프로세스 기동, 탐색기 `Space` 미리보기 조작·변환, 제거 및 설정 해시 보존 확인 | (a) CLI 허용 명령이 `dotnet build`, `dotnet test` 등으로 제한되어 `scripts/Test-Installer.ps1` 및 `MoaView-0.1.12-Setup-x64.exe` 직접 실행 불가, (b) 작업 규칙상 설치·권한 변경·배포 금지, (c) 호스트 계정의 기존 MoaView 설치 보호 및 Windows Sandbox/VM/별도 계정 전환 도구 미제공 |

## 3. 코드 보완 및 격리 환경 실제 검증 내역

### 3.1 탐색기 창 종료·재시작 시 이전 미리보기 정리 보강

- **`src/MoaView/Services/ExplorerWindowContext.cs`**:
  - Win32 `IsWindow` API를 래핑한 `ExplorerWindowContext.IsWindow(nint window)`를 추가해 원본 탐색기 최상위 창 핸들의 유효성을 즉시 확인할 수 있도록 했습니다.
- **`src/MoaView/Services/ExplorerPreviewCoordinator.cs` 및 `ExplorerPreviewIntegration.cs`**:
  - 기존 `RefreshAsync()`는 탐색기 창이 닫혀 포그라운드 포커스가 바탕 화면·작업 표시줄 등 탐색기 밖(`getCurrentContext() == null`)으로 이동하면 원본 탐색기 창(`_source.WindowHandle`)이 파괴되었더라도 `Close()`를 호출하지 않고 반환했습니다. 또한 미리보기 창이 Win32 소유 창 파괴나 `Esc` 등으로 먼저 닫힌 경우(`!presenter.IsOpen`)에도 내부 `_source`·`_path` 상태를 초기화하지 않았습니다.
  - `ExplorerPreviewCoordinator`에 `isWindowAlive` 판별 함수(`ExplorerWindowContext.IsWindow`)를 연결하여, `RefreshAsync()` 실행 시 (1) `!presenter.IsOpen`이면 잔여 열림 상태를 즉시 `Close()`로 정리하고, (2) `_source.WindowHandle != 0`이고 `!isWindowAlive(_source.WindowHandle)`이면 포그라운드 문맥이 `null`이더라도 즉시 `Close()`를 호출해 이전 미리보기 창과 상태를 정리하도록 수정했습니다.
- **`src/MoaView/Services/ExplorerPreviewPresenter.cs`**:
  - `Show(string path, ExplorerContext context)`에서 이미 열린 미리보기 창을 재사용할 때도 `context.WindowHandle != 0`이면 `WindowInteropHelper(Window).Owner`를 현재 탐색기 창 핸들로 갱신하도록 보완했습니다.

### 3.2 C2 잔여 시나리오 3건 자동 검증 추가 (`tests/MoaView.Tests/ExplorerPreviewTests.cs`)

1. **`SpaceGesture_LongPressConsumesTypematicRepeatsAndRecoversLostKeyUpOnlyAfterQuietPeriod`**:
   - `Space` 첫 눌림(`Toggle`) 이후 25회 연속 키 반복(`isDown: true`)이 모두 `Consume`으로 처리되고, 물리적 키가 눌려 있거나(`physicallyDown: true`) 마지막 이벤트 후 `1400ms`만 지난 시점에는 `RecoverReleasedKey`가 상태를 초기화하지 않으며, 키-업 유실 후 `1500ms` 경과 시에만 복구되어 다음 `Space`가 정상 동작함을 검증했습니다.
2. **`HookAndPresenter_LongPressOpensAndClosesRealPreviewWindowOnceWhilePeriodicRenewalRuns`**:
   - 실제 네이티브 `WH_KEYBOARD_LL` 훅(`ExplorerSpaceKeyHook`)이 주기적으로 재등록(`RegistrationCount >= 2`, `RegistrationHandle != 0`)되는 동안, `Space` 길게 누르기(초기 `KeyDown` + 15회 반복 `KeyDown` + `KeyUp`) 시 실제 WPF 미리보기 창이 단 1회만 열려 이미지를 렌더링(`VerifyRenderedState() == true`, `ReadCount == 1`)하고, 두 번째 길게 누르기 시 단 1회만 닫힘(`presenter.IsOpen == false`)을 확인했습니다.
3. **`PresenterAndCoordinator_TabSwitchInSameExplorerWindowUpdatesRenderedPreviewAndImmediateSpaceSwitchesTab`**:
   - 동일 탐색기 창(`explorerHwnd`) 내 5개 탭(`ViewHandle` 101~105)에서:
     - 탭 1(`PNG`) 열기 → 탭 2(`BMP`)로 전환 직후 200ms 타이머 전에 `Space` 입력 시 닫히지 않고 즉시 탭 2 이미지로 전환 렌더링 → 탭 3(`ZIP`)으로 전환 후 주기 갱신(`RefreshAsync`) 시 동일 미리보기 창 인스턴스에서 아카이브 목록으로 전환 → 탭 3과 같은 파일 경로가 선택된 탭 4로 전환 후 `Space` 입력 시 정상 닫기 → 다시 탭 1에서 연 뒤 선택이 없는 탭 5로 전환 시 자동 닫기를 실제 WPF 창으로 확인했습니다.
4. **`PresenterAndCoordinator_ExplorerWindowCloseCleansUpPreviewAndRestartedExplorerOpensNewPreview`**:
   - 첫 번째 탐색기 창(`hwnd1`)에서 미리보기를 열고 렌더링을 확인한 뒤 `explorerWindow1.Close()`로 창을 종료(`ExplorerWindowContext.IsWindow(hwnd1) == false`)하고 포커스를 탐색기 밖(`activeContext = null`)으로 두었을 때, `RefreshAsync()`가 이전 미리보기 창과 상태를 정리(`presenter.IsOpen == false`)하고, 새 탐색기 창(`hwnd2`)을 띄워 `Space`를 누르면 새 소유 핸들(`Owner == hwnd2`)로 미리보기 창이 다시 열려 렌더링됨을 확인했습니다.

### 3.3 C9 격리 설치·실행·제거 검증 보강 (`tests/MoaView.Tests/DeploymentAndGuideVerificationTests.cs`)

- `IsolatedInstallRunSpacePreviewAndUninstall_PreservesUserSettingsAndCleansUpRegistrations`에서 `scripts/Test-Installer.ps1`가 검사하는 모든 기동 항목과 일치하도록 다음 검증을 추가했습니다.
  - 격리 설치본 `MoaView.exe --verify-startup`으로 `PPM` 이미지(`ImageLoaded`), `Rive` 공식 픽스처(`RiveLoaded` 및 호스트 WebView2 런타임 유무에 따른 `|RIVE=ready|` 또는 `|RIVE=error|`), `SVG` 표본(`SpecializedLoaded` 및 `|SPECIALIZED=ready|` 또는 `|SPECIALIZED=error|`) 렌더링 및 테마(`|THEME=Light|MDTHEME=Sepia`) 적용 확인.
  - 격리 레지스트리 `Classes\*\shell\MoaView.Preview\command`에 실제 저장된 명령 문자열(`"<installedExe>" --preview "%1"`)을 읽어 `"%1"`에 표본 파일 경로를 대입한 뒤 프로세스를 기동하여 우클릭 메뉴 명령 계약이 정상 동작함을 확인.

## 4. 실제 실행한 검증 결과 및 남은 외부 작업

| 검증 항목 | 실행 명령 | 결과 | 비고 |
| --- | --- | --- | --- |
| 솔루션 빌드 | `dotnet build` | 통과 (경고 0, 오류 0) | `MoaView`, `MoaView.Tests` 컴파일 성공 |
| 전체 자동 회귀 및 격리 환경 실검증 | `dotnet test` | 통과 (191 / 191, 실패 0, 건너뜀 0) | 신규 C2 탐색기 잔여 시나리오 테스트 4개(`ExplorerPreviewTests` 19개) 및 C9 격리 설치 `PPM`·`Rive`·`SVG`·우클릭 레지스트리 명령 검증(`DeploymentAndGuideVerificationTests` 3개) 포함 |
| 별도 깨끗한 Windows OS(테스트 계정 / Sandbox / VM)에서 `Test-Installer.ps1` 및 실제 Windows 11 탐색기 대화형 탭·재시작 수동 확인 | 미실행 (환경 및 명령 제약) | 외부 실환경 필요 | 아래 4.1절의 재현 절차 참조 |

### 4.1 외부 실환경에서 남은 수동·설치 확인을 마치는 최소 절차

1. **현재 Windows 11 데스크톱에서 탐색기 대화형 확인 (C2 수동 확인 3건)**:
   - 탐색기에서 폴더 탭 2개를 열어 파일 선택 후 탭 전환(`Ctrl+Tab` 또는 마우스 클릭), `Space` 2초 이상 길게 누르기, 미리보기가 열린 상태에서 탐색기 창 닫기(`Alt+F4`) 후 새 탐색기 창(`Win+E`)을 열어 `Space` 동작을 확인합니다 (`docs/explorer-preview.md` 표 기준).
2. **별도 깨끗한 Windows 계정 / Windows Sandbox / VM에서 설치·제거 확인 (C9)**:
   - `docs/QUIC-9-isolated-environment-verification.md` 4.2절 및 `docs/install.md`의 절차에 따라 MoaView 미설치 환경에서 `scripts/Test-Installer.ps1 -Installer artifacts/installer/MoaView-0.1.12-Setup-x64.exe` 실행 및 Windows 설정 앱 제거를 확인합니다.
