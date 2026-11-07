# QUIC-1 저장소·검증 경로 조사 및 충돌 조정

- 확인일: 2026-11-07 (Asia/Seoul)
- 조사 대상: 현재 원본 main aea2e3d7f86455a3d8289486f2c06dfed20617dc
- 기존 작업 기준: 281805ba6965c10bd174bffa7e6a53efe74a6945 (초기 README만 있는 저장소)
- 범위: 기존 변경 보존, 현재 구현·검증 경로 확인, 오래된 작업 결과와 현재 원본의 충돌 해소. 제품 전체 완료 판정은 별도다.

## 충돌 원인과 보존

QUIC-1의 과거 재개 실행은 초기 저장소를 기준으로 MoaView.App/MoaView.Core와 6개 테스트, 별도 오프라인 복원 설정을 만들었다. 그 사이 원본에는 QUIC-2~4와 후속 개발이 반영되어 src/MoaView, xUnit 테스트, 탐색기 연동·다양한 형식·설치·테마 기능이 추가됐다. 양쪽의 솔루션, 빌드 설정, 테스트 프로젝트 경로가 겹쳐 WorkMesh가 자동 반영을 차단했다.

현재 원본의 앱 소스·빌드 설정·테스트·설치·테마 파일은 유지한다. 오래된 별도 앱/코어 및 수제 테스트 구성을 현재 솔루션에 덮어쓰지 않는다. 기존 작업 폴더의 소스와 산출물 99개(1,231,399바이트), 원래 조사 문서, 실행 이력과 Git 포인터를 WorkMesh 로컬 복구 보관소의 QUIC-1-20261107-8d6a3601-0d67-49c5-83c3-b4a05e5f9c0a에 보존했다. manifest.json의 SHA-256과 모든 복사본을 대조했다. 이번 반영 대상은 갱신한 조사 문서 한 개이며, 과거 코드와 보고서를 버린 것이 아니다.

## 현재 구조와 중복 작업

현재 MoaView.sln은 src/MoaView/MoaView.csproj와 tests/MoaView.Tests/MoaView.Tests.csproj를 포함한다. 앱은 .NET 8 WPF(net8.0-windows10.0.19041.0), 버전 0.1.7이며 xUnit 2.5.3과 Microsoft.NET.Test.Sdk 17.8.0을 사용한다. README와 docs/install.md, docs/explorer-preview.md, docs/content-preview.md, docs/third-party-notices.md가 현재 사용·검증·제한의 기준 문서다. 저장소 전용 AGENTS.md는 없다.

WorkMesh에서 QUIC-2(기본 앱), QUIC-3(이미지 조작), QUIC-4(변환), QUIC-5(ZIP 미리보기)는 완료 상태다. 일감 완료와 원본 반영은 다르며 QUIC-5는 원본 반영 이력이 없는 별도 결과가 남아 있다. 현재 원본에 이미 있는 기능을 과거 QUIC-1의 누락 판정만 보고 다시 구현하지 않는다. 초기 Git 이력과 현재 자체 네임스페이스·구조를 확인했으며, 이것만으로 외부 코드의 모든 유래를 증명했다고 주장하지 않는다. 포함한 외부 라이브러리는 별도 라이선스 안내를 따른다.

## C1–C10 근거와 남은 확인

| 기준 | 현재 저장소 근거 | 남은 확인 |
| --- | --- | --- |
| C1 독립 Windows 앱 | src/MoaView, 독립 솔루션과 EXE 시작 검증 코드 | 현재 빌드의 실행·배포 환경 검증 |
| C2 탐색기 Space | ExplorerPreviewIntegration, ExplorerSpaceKeyHook, ExplorerPreviewTests | 실제 탐색기 입력·검색·이름 변경·Windows 10/11 탭 동작 |
| C3 이미지 보기 | ImagePreviewService, FolderImageNavigationService, MainWindowViewModelTests | 최신 변경 회귀 및 실제 파일 확인 |
| C4 변환 | ImageConversionService, ImageConversionServiceTests | 최신 빌드의 변환·원본 보호 회귀 |
| C5 보기 조작 | 확대·맞춤·회전·이전/다음 UI와 MainWindowUiTests | 실제 키보드·마우스 흐름 검증 |
| C6 압축 미리보기 | SafeZipArchive, AdditionalArchiveReader, ContentPreviewTests | 실제 압축 샘플과 오류·제한 확인 |
| C7 형식/미지원 안내 | SupportedFileFormats, README, docs/content-preview.md | 문서·실제 동작 일치와 제한 검증 |
| C8 회귀 검증 | xUnit 테스트 14개 클래스 파일, WPF/독립 EXE 검증 코드 | 현재 전체 테스트 재실행 결과와 환경 제약 구분 |
| C9 설치/안내 | installer, scripts/Build-Installer.ps1, docs/install.md | 깨끗한 Windows 설치·제거·런타임 안내 확인 |
| C10 반복·독립 검증 | WorkMesh 기존 일감·검토·로컬 반영 이력 | 현재 원본 기준으로 검증과 다음 작은 목표 재개 |

코드와 테스트의 존재를 해당 기준의 전체 통과로 판정하지 않는다. README가 남긴 C2 실환경 검증과 C8·C9 배포 환경 검증은 계속 유효하다.

## 이번 실제 검증 기록

현재 원본 aea2e3d7f86455a3d8289486f2c06dfed20617dc의 추적 파일 126개를 별도 검증 사본으로 내보냈다. 원본 작업 파일과 기존 QUIC-1 코드를 빌드 실험으로 수정하지 않았다.

1. dotnet build MoaView.sln -m:1 -nr:false -p:RestoreIgnoreFailedSources=true: 종료 코드 1. NuGet 복원 단계의 GetRestoreSettingsTask에서 Value cannot be null (path1), 오류 2건. 컴파일·제품 테스트 전 단계다.
2. NuGet.Config·패키지 저장 경로·기존 사용자 캐시 소스를 명시한 빌드: 종료 코드 1. NuGet.targets 복원 단계에서 같은 path1 오류 1건. 기본 경로를 명시하는 시도로 복구되지 않아 같은 명령을 반복하지 않았다.

3. dotnet test MoaView.sln -m:1 -nr:false -p:RestoreIgnoreFailedSources=true: 종료 코드 1. 두 프로젝트가 같은 NuGet path1 복원 오류로 중단되어 테스트 수집·실행에 도달하지 못했다.

환경은 Windows의 .NET SDK 8.0.200이며, 이번 검증 프로세스의 제한된 실행 환경에서 확인했다. 로그는 WorkMesh .local/recovery 아래 별도 QUIC-1-validation-414014cb-5eeb-4270-84ac-b662fdaffdfd 사본에 보존했다. 이번 실행에서 최신 원본의 빌드/테스트가 통과했다고 보고하지 않는다. 과거 6개 테스트 성공은 보관한 초기 프로토타입의 결과이며 현재 원본의 xUnit 결과가 아니다.

## 다음 작업

현재 원본에서 README의 dotnet build 및 dotnet test를 실행하고 실제 종료 코드·테스트 수를 기록한다. NuGet 기본 경로 또는 VSTest localhost 통신 제한은 제품 소스 결함과 구분한다. 환경 오류 때문에 테스트를 무력화하거나 테스트 프로젝트를 옛 수제 실행기로 바꾸지 않는다. 검증 후 현재 README에 남아 있는 C2 실환경 검증과 C8·C9 배포 환경 검증을 작은 단위로 이어간다.
