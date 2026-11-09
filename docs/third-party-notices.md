# 외부 구성 요소

## Rive Canvas

- 패키지: `@rive-app/canvas` **2.49.3** (정확한 버전 고정)
- 공급처: [공식 npm 패키지](https://www.npmjs.com/package/@rive-app/canvas/v/2.49.3)
- 취득: `npm pack @rive-app/canvas@2.49.3 --ignore-scripts`, 2026-11-05
- 포함 파일: `src/MoaView/Assets/Rive/vendor/rive.js`, `rive.wasm`, `rive_fallback.wasm`, `package.json`
- 라이선스: MIT, Copyright (c) 2020-2021 Rive. [전체 라이선스](../src/MoaView/Assets/Rive/vendor/LICENSE)를 앱 출력에도 복사합니다.
- 업데이트 시 공식 npm 패키지를 다시 받고 아래 해시와 실제 RIV 렌더링 테스트를 갱신해야 합니다. 빌드나 실행 시 npm/CDN에서 자동으로 버전을 올리지 않습니다.

| 파일 | SHA-256 |
| --- | --- |
| rive.js | `12eaa68f25f36fde5d5b452b7aa93870cc8d34551fc8033301f4f3fd0acede13` |
| rive.wasm | `10d9524559fe8294615c26ffaad34d422630f4dcc9dc4ccc982d0496aa5bf55d` |
| rive_fallback.wasm | `157165c3de27b6c4a2cadc97518005ef38fc6f6f58ff5a5be48842298f69d9fc` |

## Rive 테스트 샘플

`tests/MoaView.Tests/Fixtures/data_bind_runtime_test.riv`는 [공식 rive-wasm 저장소의 테스트 자산](https://github.com/rive-app/rive-wasm/blob/master/js/test/assets/data_bind_runtime_test.riv)에서 2026-11-05에 취득했습니다. 동일 저장소의 MIT 라이선스를 [LICENSE-Rive](../tests/MoaView.Tests/Fixtures/LICENSE-Rive)에 보관합니다. 앱 배포용 샘플이 아니라 자동 검증에 사용합니다.

SHA-256: `59b04c026829e41140e3ac37cd9c75fcac303886689999e79fb5baf04c066b39`.

## Microsoft WebView2

`Microsoft.Web.WebView2` NuGet 패키지 **1.0.4314.35**를 참조합니다. [패키지 정보 및 라이선스](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35)를 확인하세요. 실제 재생용 Microsoft Edge WebView2 Runtime은 사용자 환경에 설치되어 있어야 하며 이 저장소에는 브라우저 런타임을 재배포하지 않습니다.

## Markdig

마크다운 구문 분석에 [공식 Markdig NuGet 패키지 1.5.1](https://www.nuget.org/packages/Markdig/1.5.1)을 사용합니다. .NET 8 대상의 추가 패키지 의존성은 없으며 버전을 고정했습니다. 렌더링은 본 프로젝트의 WPF 코드로 처리합니다. BSD-2-Clause 라이선스, Copyright (c) 2016-2026 Alexandre Mutel. [전체 라이선스](../src/MoaView/Assets/Licenses/Markdig.txt)는 앱 출력 및 배포 폴더에 포함합니다.

## SharpCompress

RAR·7Z·TAR 목록과 내부 항목을 읽기 위해 [공식 SharpCompress NuGet 패키지 0.50.8](https://www.nuget.org/packages/SharpCompress/0.50.8)을 사용합니다. [MIT 라이선스](../src/MoaView/Assets/Licenses/SharpCompress.txt)는 앱 출력 및 배포 폴더에 포함합니다.

## Magick.NET과 BPG

- 전문 이미지 형식은 Apache-2.0 라이선스의 Magick.NET-Q8-x64 **15.1.0**으로 읽습니다. NuGet 패키지의 ImageMagick·LibRaw·libheif 등 구성 요소 고지를 `Assets/Licenses/Magick.NET-Notices.txt`로 복사합니다. [공식 릴리스](https://github.com/dlemstra/Magick.NET/releases/tag/15.1.0) · [라이선스](../src/MoaView/Assets/Licenses/Magick.NET-Apache-2.0.txt)
- BPG 표시에는 Fabrice Bellard의 libbpg **0.9.8** 배포판에 포함된 독립 JavaScript 디코더 두 개를 사용합니다. BPG 코드는 BSD, 포함된 FFmpeg 디코더는 LGPL 2.1입니다. 설치본의 `Assets/Licenses`에 대응 소스 `libbpg-0.9.8.tar.gz`를 포함하며 실행 시 네트워크에서 코드를 가져오지 않습니다. [공식 소스](https://bellard.org/bpg/libbpg-0.9.8.tar.gz) · [원본 고지](../src/MoaView/Assets/Licenses/LibBpg-README.txt) · [LGPL 2.1](../src/MoaView/Assets/Licenses/LibBpg-LGPL-2.1.txt)

| libbpg 배포 파일 | SHA-256 |
| --- | --- |
| `libbpg-0.9.8.tar.gz` | `e2e5518fb3ae0883b79e857c640aa15bfc3a4dae50df1096a2cdf3e5a17fe1f` |
| `bpgdec.js` | `e532951a2f770a1af1a550e24b8bfdaad578d587303e7fd87c19ad76930da46e` |
| `bpgdec8a.js` | `6ed7bfe02317130c69a27d66a6e67fdaeac5a44b5f9c4849205c2f628d44ac8c` |

Clip Studio Paint는 공개 구조 조사에서 확인된 컨테이너와 캔버스 미리보기 테이블을 Windows의 SQLite로 읽습니다. 테스트의 CLIP 파일은 SQLite와 PNG로 생성한 합성 자료입니다. [CLIP 컨테이너 구조 조사](https://github.com/Aodaruma/clipfile-rs/blob/main/docs/format-analysis.md) · [Windows SQLite API](https://learn.microsoft.com/en-us/uwp/win32-and-com/win32-extension-apis)

## 문서 형식 구현 참고

- [Microsoft Open XML 문서 구조](https://learn.microsoft.com/office/open-xml/about-the-open-xml-sdk)
- [한컴 HWP 5.0 형식 명세](https://cdn.hancom.com/link/docs/%ED%95%9C%EA%B8%80%EB%AC%B8%EC%84%9C%ED%8C%8C%EC%9D%BC%ED%98%95%EC%8B%9D_5.0_revision1.2.pdf)
- [한컴 HWPX 패키지 구조](https://tech.hancom.com/python-hwpx-parsing-1/)

문서 파서는 본 프로젝트의 구현이며 Office·한글 프로그램 또는 다른 미리보기 앱의 소스를 포함하지 않습니다.

## three.js 3D 모델 렌더러

- 패키지: [공식 npm `three` 0.187.0](https://www.npmjs.com/package/three/v/0.187.0), MIT License, Copyright © 2010-2026 three.js authors.
- 취득: `npm pack three@0.187.0`, 2026-11-10. npm 패키지 tarball SHA-256: `ff540832ded0cfe919764d24a3d88f3e2a05f23303f876423e4f893841e90728`.
- 포함 파일: `src/MoaView/Assets/ThreeD/vendor/build/three.module.js`, `three.core.js`와 선택한 `examples/jsm` 애드온·파서의 필요한 의존 파일만 포함합니다. [공식 three.js 저장소](https://github.com/mrdoob/three.js)에서 배포한 파일이며 [패키지의 전체 MIT 라이선스](../src/MoaView/Assets/ThreeD/LICENSE)를 앱 출력에도 복사해야 합니다.
- 포함된 `fflate.module.js` **0.8.3**은 three.js의 FBX 압축 파서가 사용하는 라이브러리입니다. MIT License, Copyright © 2026 Arjun Barrett. [전체 라이선스](../src/MoaView/Assets/ThreeD/vendor/examples/jsm/libs/LICENSE-fflate.txt)는 앱 출력에도 복사해야 합니다. [공식 fflate 저장소](https://github.com/101arrowz/fflate).
- 렌더러는 three.js `WebGLRenderer`, `OrbitControls`, `GLTFLoader`, `FBXLoader`, `OBJLoader`, `MTLLoader`, `STLLoader`, `PLYLoader`를 사용합니다. 모델은 선택적으로 재생하지 않는 한 애니메이션을 정지 상태로 표시합니다. Draco, KTX2, Meshopt 외부 디코더는 포함하지 않습니다.
- 앱은 로컬 고정 자산으로만 실행하며 npm/CDN에서 코드를 가져오지 않습니다. three.js 버전을 바꿀 때 import된 애드온의 의존 파일과 양쪽 라이선스, 패키지 SHA-256을 함께 확인하세요.
