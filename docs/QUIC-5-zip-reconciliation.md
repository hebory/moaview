# QUIC-5 ZIP 결과 보존 및 현재 원본과의 조정

- 확인일: 2026-11-07
- 현재 원본: bf4ea057541197fb668de7877ff3253550d0b04a (앱 코드는 aea2e3d와 동일)
- 과거 QUIC-5 기준: c860bc564724b79633aa2950d00b1d5b7c7dae4d

## 정리 이유

QUIC-5는 사용자가 실행 결과를 확인해 일감은 완료됐지만 원본 반영 이력이 없어 후속 자율 작업의 선행 조건을 막고 있었다. 그 사이 원본에는 별도 후속 개발로 ZIP을 포함한 공용 콘텐츠 미리보기와 새 화면이 구현됐다. 과거의 ZipEntryList/ZipTextPreview UI와 ZipPreviewService 전체를 덮어쓰면 현재 압축·문서·테마 기능을 되돌리므로, 현재 코드를 유지하고 두 결과를 대조한 기록을 반영한다.

과거 작업 폴더 전체 212개 파일(7,083,634바이트), 실행 이력, Git 포인터는 WorkMesh .local/recovery/QUIC-5-20261107-21f68bb6-343e-41f8-a0f7-4878bb8ea5ee에 보존했다. manifest.json의 SHA-256과 복사본을 대조했다. 기존 소스, ZipPreviewUiTests.cs, build-diagnostic.log 및 빌드 산출물이 포함된다. 이전 변경의 삭제나 동일 기능 회귀 검증 완료를 의미하지 않는다.

## 구현 대조

| 이전 결과 | 현재 원본의 대응 구현 | 판단 |
| --- | --- | --- |
| ZipPreviewService.List/Load, ZipItem | FilePreviewService, ArchivePreview, ArchiveEntryInfo, SafeZipArchive | 현재 공용 압축 모델 유지 |
| 암호화·분할·ZIP64·손상 헤더 검사 | SafeZipArchive.ReadDirectory/Read의 플래그·범위·분할·ZIP64·CRC 검사 | 과거 서비스로 교체하지 않음 |
| 파일이 변경된 뒤 항목 인덱스 검증 | 읽은 바이트를 보존한 ArchivePreview 스냅샷과 항목 소유 확인 | 현재 스냅샷 구조 유지 |
| 비동기 ZIP 결과 UI 스레드 적용 및 이전 결과 덮어쓰기 방지 | 현재 SelectedArchiveEntry는 동기 LoadArchiveEntry/ApplyResult 호출 | 이전 비동기 패치 직접 이식 대상이 아님. UI 응답성은 별도 검증 필요 |
| ZIP 항목·텍스트 전용 화면 | ArchiveEntryList, PreviewText, MainWindowViewModel의 공용 콘텐츠 상태 | 현재 이미지·문서·압축 통합 UI 유지 |
| ZIP 안내와 형식 필터 | SupportedFileFormats, README, docs/content-preview.md | 현재 안내와 현재 제한값 유지. 과거 64 MiB/2000항목 제한을 덮어쓰지 않음 |

현재 ContentPreviewTests는 ZIP 목록·이미지·텍스트·바이너리 안내, 디스크 추출/파일 잠금 방지, 암호화·CRC·크기·압축 방식 오류, 빈 파일·손상 파일, 실제 WPF 창의 항목 전환 및 일반 이미지 복귀를 검증하는 코드를 포함한다.

## 보존한 고유 회귀 시나리오와 남은 작업

과거 ZipPreviewUiTests의 ZIP 내부 JPEG·PNG·BMP·GIF·TIFF 전체 UI 순회와 암호화 항목의 실제 창 안내는 현재 UI 이름과 상태 모델에 맞춰 회귀 시나리오를 재검토할 가치가 있다. 현재 테스트 코드의 존재만으로 이 모든 시나리오가 동일하게 통과했다고 판정하지 않는다. 과거 비동기 처리 중 다른 파일 열기 테스트는 현재 동기 구조에서 그대로 실행할 수 없으므로 새 UI 상태 복귀·응답성 검증으로 검토한다. 실제 암호화 도구가 만든 샘플 검증도 남아 있다.

과거 기록의 60개 테스트 통과와 기본 dotnet build 실패는 과거 작업 폴더의 결과다. 최신 앱의 통과 근거가 아니다. 이번 최신 소스 검증은 QUIC-1-repository-and-verification-survey.md에 기록한 것처럼 .NET SDK 8.0.200의 제한된 실행 환경에서 NuGet path1 복원 오류로 빌드와 테스트가 시작 단계에서 중단됐다. 동일 명령을 반복하거나 테스트를 비활성화하지 않았다.

후속 작업은 현재 원본에서 빌드·테스트 실행 환경을 확인하고 실제 회귀 결과를 기록해야 한다. 위 고유 UI 시나리오와 실제 샘플, 현재 README의 C2·C8·C9 누락 검증을 검토한다. 이 조정은 중복된 과거 구현의 반영 대기 상태를 정리하는 것이며 제품 전체 C1–C10 완료 판정이 아니다.
