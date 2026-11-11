# QUIC-7 실사용 및 복구 검증

작성일: 2026-11-12 (Asia/Seoul)

## 상태와 보존

QUIC-6은 `32be002994224bef7d3c8ca0073cc271a783b29b`로 반영됐으며 당시 독립 검증의 `dotnet build`와 178개 테스트가 통과했다. 후속 QUIC-7의 첫 실행은 Agy `CLI_PERMISSION_REQUIRED`로 종료됐다. 정확한 거부 명령 문자열은 저장 기록에 없어 특정하지 않았다. 시작 시 원본과 QUIC-7 작업 폴더는 모두 이 HEAD였고 Git 변경 사항이 없었다. 기존 승인 범위나 CLI 설정은 넓히지 않았다.

현재 복구는 기존 QUIC-7 격리 작업 폴더에서 진행한다. 이전 실행 기록과 검증 표본·결과는 WorkMesh `.local/recovery/QUIC-7-20261112-ec20bbf0-be3d-49b9-bbf5-fff32b2772b3`에 보존했다. 사용자 원본 파일 대신 새 표본 폴더를 사용했고 이름 변경 검사는 취소해 파일 이름을 유지했다.

## 수정

다른 탐색기 창 또는 탭으로 이동한 직후 Space를 누르면 200ms 주기 갱신보다 빨라 기존 미리보기만 닫히던 경계를 수정했다. 같은 뷰에서 Space를 누르면 계속 닫히고, 다른 뷰에서는 새 선택을 연다. 이전 Shell 조회가 끝나지 않았으면 마지막 열기 대상 하나만 보류한다. 닫기·종료·포커스 이동 뒤의 오래된 응답은 창을 다시 열거나 이전 파일로 덮어쓰지 않는다. 선택이 없는 뷰에서는 닫힌 상태를 유지한다.

새 `scripts/Test-Portable.ps1`은 자체 포함 게시물을 새 폴더로 복사해 실행한다. 기존 설치·레지스트리를 변경하지 않고, 버전·런타임·필수 파일·실제 창 렌더링과 사용자 설정 전후 해시를 확인한다. 한글 메시지가 있는 스크립트는 Windows PowerShell 5.1 호환 UTF-8 BOM으로 저장했다.

## 실제 확인 결과

| 확인 | 결과 및 범위 |
| --- | --- |
| 수정 전 탐색기 회귀 | 15개 통과 |
| 다른 창 즉시 Space·지연 응답 회귀 | 수정 전 두 사례 실패, 수정 후 통과 |
| 전체 빌드·기능 회귀 | 경고 0·오류 0, 181개 통과(취소 보강 전) / 재개 실행에서 전체 184개 통과(실패 0·건너뜀 0) |
| 닫기·종료·포커스 이동 취소 보강 | Coordinator 13개 통과, 새 취소 3개 포함 |
| 자체 포함 Release 게시 | .NET/Windows Desktop 8.0.33, win-x64 성공 |
| 새 폴더에서 실제 EXE 실행 | 빈 창, JPEG·PNG·BMP·정적 GIF·TIFF, ZIP 목록, 손상 JPEG 오류, 미지원 바이너리 안내 9개 통과 |
| 사용자 설정 보존 | app-theme 해시 동일, markdown-theme 파일 생성·변경 없음 |
| 실제 탐색기 Space | 현재 설치된 0.1.12 / Windows 11 26H2 빌드 26300에서 PNG 열기, GIF·바이너리·손상 PNG·ZIP 선택 전환, Space 닫기 확인 |
| 실제 입력 보존 | 주소창·검색창·이름 변경에 Space 공백 정상 입력, 미리보기 미실행 확인 |

무설치 검사 결과는 `portable-check/6a847f5c1d0f458984f4776114fe6bfb/result.json`에 저장했다. 손상 JPEG의 디코더 오류 출력은 의도한 오류 표본이며 앱의 정상 오류 화면과 종료 코드 0을 확인했다. ZIP 내부 이미지·텍스트 선택, 이미지 조작·변환과 WebView2 기능은 WPF 기능 회귀에 포함된다. 이번 무설치 smoke의 ZIP 검사는 목록 화면까지, GIF는 정적 첫 프레임까지 확인한다.

## 재현

```powershell
dotnet build
dotnet test
dotnet publish src/MoaView/MoaView.csproj -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=8.0.33 -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/quic7-publish/win-x64 -m:1
powershell.exe -NoProfile -File scripts/Test-Portable.ps1 -PublishDirectory artifacts/quic7-publish/win-x64
```

기존 설치의 상주 모아뷰가 켜져 있으면 새 EXE 실행도 기존 프로세스로 전달된다. 새 무설치 앱을 직접 쓰려면 기존 앱의 트레이 메뉴에서 완전히 종료하고 새 폴더의 `MoaView.exe`를 실행한다. 기본 창 닫기는 트레이 상주를 유지하므로 완전 종료와 다르다.

## 완료 판단의 한계

현재 PC의 실제 사용과 최신 수정의 기능 회귀·무설치 기동 근거를 확보했다. 깨끗한 Windows 계정/VM 설치·제거, WebView2가 없는 환경, Windows 10, Windows 11 탭 전환, 장시간 사용·절전 복귀는 이번 검증으로 통과 처리하지 않는다. 현재 계정에는 모아뷰가 설치되어 있어 기존 설치를 변경하는 `Test-Installer.ps1`은 실행하지 않았다. 이 환경 검증이 없는 상태에서 C1–C10 전체 완료를 선언하지 않는다.
