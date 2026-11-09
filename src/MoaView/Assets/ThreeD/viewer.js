import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { FBXLoader } from 'three/addons/loaders/FBXLoader.js';
import { OBJLoader } from 'three/addons/loaders/OBJLoader.js';
import { MTLLoader } from 'three/addons/loaders/MTLLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { PLYLoader } from 'three/addons/loaders/PLYLoader.js';

const canvas = document.getElementById('canvas');
const status = document.getElementById('status');
const stats = document.getElementById('stats');
const animationSelect = document.getElementById('animation');
const animationLabel = document.getElementById('animation-label');
const buttons = Object.fromEntries(['fit', 'wire', 'grid', 'rotate', 'play', 'stop'].map(id => [id, document.getElementById(id)]));
const MODEL_ORIGIN = 'https://moaview-3d.invalid';
const controls = { renderer: null, scene: null, camera: null, orbit: null, grid: null, model: null, pivot: null,
  mixer: null, clips: [], activeAction: null, loaded: false, wireframe: false, gridVisible: true, autoRotate: false,
  isDark: true, background: '#14161a', generation: 0, animationFrame: 0, previousTime: 0, box: null };
controls.lastError = '';

function post(message) { try { window.chrome.webview.postMessage(message); } catch { /* Detached preview. */ } }
function report(type, message) { status.textContent = message; post({ type, message }); }
function updateButtonState(id, pressed) { buttons[id].setAttribute('aria-pressed', String(pressed)); }
function enableTools(enabled) {
  for (const button of Object.values(buttons)) button.disabled = !enabled;
  animationSelect.disabled = !enabled || controls.clips.length === 0;
  buttons.play.disabled = !enabled || controls.clips.length === 0;
  buttons.stop.disabled = !enabled || controls.clips.length === 0;
}

function ensureRenderer() {
  if (controls.renderer) return;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false, powerPreference: 'low-power' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.1;
  controls.renderer = renderer;
  controls.scene = new THREE.Scene();
  controls.scene.background = new THREE.Color(controls.background);
  controls.camera = new THREE.PerspectiveCamera(45, 1, 0.01, 10000);
  controls.camera.position.set(3, 2.4, 4);
  controls.orbit = new OrbitControls(controls.camera, renderer.domElement);
  controls.orbit.enableDamping = true;
  controls.orbit.dampingFactor = 0.075;
  controls.orbit.screenSpacePanning = true;
  controls.orbit.target.set(0, 0, 0);
  controls.scene.add(new THREE.HemisphereLight(0xffffff, 0x505060, 2.2));
  const keyLight = new THREE.DirectionalLight(0xffffff, 3.0);
  keyLight.position.set(4, 6, 5);
  controls.scene.add(keyLight);
  const fillLight = new THREE.DirectionalLight(0x9dbbff, 1.2);
  fillLight.position.set(-5, 2, -4);
  controls.scene.add(fillLight);
  controls.grid = new THREE.GridHelper(10, 20, 0x718096, 0x384252);
  controls.grid.name = '__moaview_grid__';
  controls.scene.add(controls.grid);
  controls.orbit.addEventListener('change', render);
  renderer.domElement.addEventListener('webglcontextlost', event => {
    event.preventDefault();
    report('error', '3D 화면이 초기화되었습니다. 파일을 다시 열어 주세요.');
  });
  resizeRenderer();
}

function resizeRenderer() {
  if (!controls.renderer || !controls.camera) return;
  const width = Math.max(1, canvas.clientWidth);
  const height = Math.max(1, canvas.clientHeight);
  controls.renderer.setSize(width, height, false);
  controls.camera.aspect = width / height;
  controls.camera.updateProjectionMatrix();
  render();
}

function render() {
  if (controls.renderer && controls.scene && controls.camera) controls.renderer.render(controls.scene, controls.camera);
}

function resourceUrl(url) {
  if (/^(?:data|blob):/i.test(url)) return url;
  try {
    const resolved = new URL(url, `${MODEL_ORIGIN}/model/`);
    if (resolved.origin !== MODEL_ORIGIN || !resolved.pathname.startsWith('/model/') || resolved.username || resolved.password)
      return `${MODEL_ORIGIN}/blocked`;
    return resolved.href;
  } catch { return `${MODEL_ORIGIN}/blocked`; }
}

function createLoadingManager(onWarning) {
  const manager = new THREE.LoadingManager();
  manager.setURLModifier(resourceUrl);
  manager.onError = () => onWarning('일부 재질 파일을 읽지 못했습니다. 가능한 모델 내용만 표시합니다.');
  return manager;
}

function fallbackTexture() {
  const canvas = document.createElement('canvas');
  canvas.width = 2;
  canvas.height = 2;
  const context = canvas.getContext('2d');
  context.fillStyle = '#ff00ff';
  context.fillRect(0, 0, 2, 2);
  return canvas.toDataURL('image/png');
}

async function loadGltf(url, manager, isCurrent) {
  const response = await fetch(url);
  if (!response.ok) throw new Error('glTF 파일을 읽을 수 없습니다.');
  const document = await response.json();
  if (!Array.isArray(document.images)) return await new GLTFLoader(manager).parseAsync(document, `${MODEL_ORIGIN}/model/`);

  const fallback = fallbackTexture();
  const objectUrls = [];
  let warning = false;
  try {
    for (const image of document.images) {
      if (typeof image.uri !== 'string' || /^(?:data|blob):/i.test(image.uri)) continue;
      const safeUrl = resourceUrl(image.uri);
      try {
        if (safeUrl.endsWith('/blocked')) throw new Error('outside the model folder');
        const asset = await fetch(safeUrl);
        if (!asset.ok) throw new Error('missing texture');
        const objectUrl = URL.createObjectURL(await asset.blob());
        objectUrls.push(objectUrl);
        image.uri = objectUrl;
      } catch {
        if (!isCurrent()) throw new Error('모델 불러오기가 취소되었습니다.');
        image.uri = fallback;
        warning = true;
      }
    }
    const result = await new GLTFLoader(manager).parseAsync(document, `${MODEL_ORIGIN}/model/`);
    return { ...result, missingTextures: warning };
  } finally {
    for (const objectUrl of objectUrls) URL.revokeObjectURL(objectUrl);
  }
}

function disposeObject(object) {
  if (!object) return;
  object.traverse(child => {
    if (!child.isMesh) return;
    child.geometry?.dispose();
    const materials = Array.isArray(child.material) ? child.material : [child.material];
    for (const material of materials) {
      if (!material) continue;
      for (const value of Object.values(material)) if (value?.isTexture) value.dispose();
      material.dispose();
    }
  });
}

function resetModel() {
  ++controls.generation;
  controls.mixer?.stopAllAction();
  controls.mixer = null;
  controls.activeAction = null;
  if (controls.pivot) {
    controls.scene.remove(controls.pivot);
    disposeObject(controls.pivot);
  }
  controls.pivot = null;
  controls.model = null;
  controls.box = null;
  controls.clips = [];
  animationSelect.replaceChildren();
  animationLabel.hidden = true;
  stats.textContent = '';
  enableTools(false);
}

async function loadModel(payload) {
  resetModel();
  const generation = controls.generation;
  if (typeof payload.url !== 'string' || !payload.url.startsWith(`${MODEL_ORIGIN}/model/`) ||
      typeof payload.extension !== 'string') {
    report('error', '모델 경로를 확인할 수 없습니다.');
    return;
  }
  status.textContent = '3D 모델을 불러오고 있습니다…';
  controls.lastError = '';
  let warning = '';
  try {
    ensureRenderer();
    const manager = createLoadingManager(message => { warning = message; });
    const extension = payload.extension.toLowerCase();
    let object;
    let clips = [];
    if (extension === '.glb' || extension === '.gltf') {
      const result = extension === '.glb'
        ? await new GLTFLoader(manager).loadAsync(payload.url)
        : await loadGltf(payload.url, manager, () => generation === controls.generation);
      object = result.scene || result.scenes?.[0];
      clips = result.animations || [];
      if (result.missingTextures) warning = '일부 텍스처를 찾지 못해 자주색 대체 무늬로 표시합니다.';
    } else if (extension === '.fbx') {
      object = await new FBXLoader(manager).loadAsync(payload.url);
      clips = object.animations || [];
    } else if (extension === '.obj') {
      const response = await fetch(payload.url);
      if (!response.ok) throw new Error('모델 파일을 읽을 수 없습니다.');
      const source = await response.text();
      const materialFile = source.match(/^\s*mtllib\s+(.+)\s*$/im)?.[1]?.trim();
      const loader = new OBJLoader(manager);
      if (materialFile && !/[\\/]/.test(materialFile) && !/^(?:[a-z]+:|\/|\\)/i.test(materialFile)) {
        try {
          const materials = await new MTLLoader(manager).loadAsync(new URL(encodeURIComponent(materialFile), payload.url).href);
          materials.preload();
          loader.setMaterials(materials);
        } catch { warning = 'OBJ 형상은 표시하지만 MTL 재질을 읽지 못했습니다.'; }
      }
      object = loader.parse(source);
    } else if (extension === '.stl') {
      const geometry = await new STLLoader(manager).loadAsync(payload.url);
      const material = new THREE.MeshStandardMaterial({ color: 0xa9b8cb, roughness: 0.62, metalness: 0.08,
        vertexColors: Boolean(geometry.hasColors), side: THREE.DoubleSide });
      object = new THREE.Mesh(geometry, material);
    } else if (extension === '.ply') {
      const bytes = await new THREE.FileLoader(manager).setResponseType('arraybuffer').loadAsync(payload.url);
      const header = new TextDecoder().decode(bytes.slice(0, Math.min(bytes.byteLength, 65536))).split('end_header')[0];
      if (!/^element\s+face\s+[1-9]\d*\s*$/m.test(header))
        throw new Error('PLY point cloud without mesh faces');
      const geometry = new PLYLoader(manager).parse(bytes);
      geometry.computeVertexNormals();
      const hasColors = geometry.hasAttribute('color');
      const material = new THREE.MeshStandardMaterial({ color: hasColors ? 0xffffff : 0xa9b8cb,
        roughness: 0.68, vertexColors: hasColors, side: THREE.DoubleSide });
      object = new THREE.Mesh(geometry, material);
    } else throw new Error('이 3D 파일 형식은 지원하지 않습니다.');

    if (generation !== controls.generation) { disposeObject(object); return; }
    if (!object) throw new Error('모델에 표시할 장면이 없습니다.');
    controls.model = object;
    controls.pivot = new THREE.Group();
    controls.pivot.add(object);
    controls.scene.add(controls.pivot);
    normalizeModel();
    controls.clips = clips;
    setupAnimations();
    controls.loaded = true;
    enableTools(true);
    updateButtonState('wire', controls.wireframe);
    updateButtonState('grid', controls.gridVisible);
    updateButtonState('rotate', controls.autoRotate);
    status.textContent = warning || '왼쪽 끌기: 회전 · 오른쪽 끌기: 이동 · 휠: 확대/축소 · F: 맞춤 · W: 선 · G: 격자 · R: 자동 회전 · Esc: 닫기';
    post({ type: 'ready' });
    render();
  } catch (error) {
    if (generation !== controls.generation) return;
    resetModel();
    const detail = error instanceof Error ? error.message : '';
    controls.lastError = detail || String(error);
    console.error('MoaView 3D preview failed:', error);
    const normalized = detail.toLowerCase();
    const message = /dracoloader|draco|meshopt/.test(normalized)
      ? 'GLB/glTF가 Draco 또는 Meshopt 압축을 사용합니다. 현재 외부 압축 디코더를 포함하지 않아 표시할 수 없습니다.'
      : /ktx2|basisu/.test(normalized)
        ? 'GLB/glTF의 KTX2/BasisU 텍스처 압축은 현재 지원하지 않습니다.'
        : '3D 파일이 손상되었거나 지원하지 않는 형식입니다.';
    report('error', message);
  }
}

function normalizeModel() {
  const box = new THREE.Box3().setFromObject(controls.model);
  if (box.isEmpty() || ![...box.min.toArray(), ...box.max.toArray()].every(Number.isFinite))
    throw new Error('모델에 표시할 형상이 없습니다.');
  const center = box.getCenter(new THREE.Vector3());
  controls.pivot.position.sub(center);
  const centeredBox = new THREE.Box3().setFromObject(controls.pivot);
  controls.box = centeredBox;
  controls.grid.position.y = centeredBox.min.y;
  controls.grid.scale.setScalar(Math.max(...centeredBox.getSize(new THREE.Vector3()).toArray(), 0.01) / 5);
  fitCamera();
  updateStats();
}

function fitCamera() {
  if (!controls.box || !controls.camera || !controls.orbit) return;
  const size = controls.box.getSize(new THREE.Vector3());
  const center = controls.box.getCenter(new THREE.Vector3());
  const radius = Math.max(size.x, size.y, size.z, 0.01) * 0.5;
  const verticalHalfFov = THREE.MathUtils.degToRad(controls.camera.fov * 0.5);
  const horizontalHalfFov = Math.atan(Math.tan(verticalHalfFov) * Math.max(controls.camera.aspect, 0.01));
  const limitingHalfFov = Math.max(0.01, Math.min(verticalHalfFov, horizontalHalfFov));
  const distance = Math.max(radius / Math.sin(limitingHalfFov) * 1.12, radius * 2.2, 0.1);
  controls.orbit.target.copy(center);
  controls.camera.position.copy(center).add(new THREE.Vector3(1.15, 0.86, 1.45).normalize().multiplyScalar(distance));
  controls.camera.near = Math.max(distance / 10000, radius / 10000, 0.001);
  controls.camera.far = Math.max(distance * 100, radius * 100, 100);
  controls.camera.updateProjectionMatrix();
  controls.orbit.minDistance = radius * 0.025;
  controls.orbit.maxDistance = radius * 250;
  controls.orbit.update();
}

function updateStats() {
  let meshCount = 0;
  let triangles = 0;
  controls.model?.traverse(child => {
    if (!child.isMesh || child.name === '__moaview_grid__') return;
    meshCount++;
    const geometry = child.geometry;
    const count = geometry.index ? geometry.index.count : geometry.attributes.position?.count || 0;
    if (geometry.groups?.length) {
      const grouped = geometry.groups.reduce((sum, group) => sum + group.count, 0);
      triangles += Math.floor((grouped || count) / 3);
    } else triangles += Math.floor(count / 3);
  });
  stats.textContent = `${meshCount.toLocaleString()}개 메시 · ${triangles.toLocaleString()}개 삼각형`;
}

function setWireframe(enabled) {
  controls.wireframe = enabled;
  controls.model?.traverse(child => {
    if (!child.isMesh) return;
    const materials = Array.isArray(child.material) ? child.material : [child.material];
    for (const material of materials) if (material && 'wireframe' in material) material.wireframe = enabled;
  });
  updateButtonState('wire', enabled);
  render();
}

function setupAnimations() {
  animationSelect.replaceChildren(...controls.clips.map((clip, index) => {
    const option = document.createElement('option');
    option.value = String(index);
    option.textContent = clip.name || `애니메이션 ${index + 1}`;
    return option;
  }));
  animationLabel.hidden = controls.clips.length === 0;
  buttons.play.hidden = controls.clips.length === 0;
  buttons.stop.hidden = controls.clips.length === 0;
  if (controls.clips.length) animationSelect.value = '0';
  controls.mixer = controls.clips.length ? new THREE.AnimationMixer(controls.model) : null;
}

function stopAnimation() {
  controls.mixer?.stopAllAction();
  controls.mixer?.setTime(0);
  controls.activeAction = null;
  render();
}

function playAnimation() {
  if (!controls.mixer || !controls.clips.length) return;
  controls.mixer.stopAllAction();
  controls.activeAction = controls.mixer.clipAction(controls.clips[Number(animationSelect.value) || 0]);
  controls.activeAction.reset().play();
}

function diagnostics() {
  let meshCount = 0;
  let triangleCount = 0;
  controls.model?.traverse(child => {
    if (!child.isMesh || child.name === '__moaview_grid__') return;
    meshCount++;
    triangleCount += Math.floor((child.geometry.index?.count ?? child.geometry.attributes.position?.count ?? 0) / 3);
  });
  return { meshCount, triangleCount, camera: controls.camera ? [controls.camera.position.x, controls.camera.position.y, controls.camera.position.z] : null,
    wireframe: controls.wireframe, gridVisible: controls.gridVisible, autoRotate: controls.autoRotate, animationCount: controls.clips.length,
    animationPlaying: Boolean(controls.activeAction?.isRunning()) };
}

buttons.fit.addEventListener('click', fitCamera);
buttons.wire.addEventListener('click', () => setWireframe(!controls.wireframe));
buttons.grid.addEventListener('click', () => { controls.gridVisible = !controls.gridVisible; controls.grid.visible = controls.gridVisible; updateButtonState('grid', controls.gridVisible); render(); });
buttons.rotate.addEventListener('click', () => { controls.autoRotate = !controls.autoRotate; controls.orbit.autoRotate = controls.autoRotate; updateButtonState('rotate', controls.autoRotate); });
buttons.play.addEventListener('click', playAnimation);
buttons.stop.addEventListener('click', stopAnimation);
animationSelect.addEventListener('change', stopAnimation);
window.addEventListener('resize', resizeRenderer);
window.addEventListener('pagehide', () => {
  cancelAnimationFrame(controls.animationFrame);
  controls.orbit?.dispose();
  resetModel();
  controls.renderer?.dispose();
  controls.renderer = null;
});
window.addEventListener('keydown', event => {
  if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return;
  const element = event.target;
  const isFormControl = element instanceof HTMLElement && (element.isContentEditable || /^(INPUT|SELECT|TEXTAREA|BUTTON)$/.test(element.tagName));
  if (event.key === 'Escape') { event.preventDefault(); post({ type: 'close' }); return; }
  if (event.code === 'Space') { if (!isFormControl) { event.preventDefault(); post({ type: 'close' }); } return; }
  if (isFormControl) return;
  const key = event.key.toLowerCase();
  if (key === 'f' && controls.loaded) fitCamera();
  else if (key === 'w' && controls.loaded) setWireframe(!controls.wireframe);
  else if (key === 'g' && controls.loaded) buttons.grid.click();
  else if (key === 'r' && controls.loaded) buttons.rotate.click();
});

window.chrome.webview.addEventListener('message', event => {
  const payload = event.data;
  if (payload.type === 'theme') {
    const colors = ['background', 'surface', 'text', 'muted', 'control', 'border'];
    if (colors.every(key => typeof payload[key] === 'string' && /^#[0-9a-f]{6}$/i.test(payload[key]))) {
      const root = document.documentElement;
      for (const key of colors) root.style.setProperty(`--app-${key}`, payload[key]);
      root.style.colorScheme = payload.isDark ? 'dark' : 'light';
      controls.isDark = Boolean(payload.isDark);
      controls.background = payload.background;
      if (controls.scene) { controls.scene.background.set(payload.background); render(); }
    }
  } else if (payload.type === 'load') loadModel(payload);
});

window.moaview3d = { diagnostics, fit: fitCamera, setWireframe, setGridVisible: visible => {
  controls.gridVisible = Boolean(visible); if (controls.grid) controls.grid.visible = controls.gridVisible;
  updateButtonState('grid', controls.gridVisible); render();
}, getLastError: () => controls.lastError };
window.chrome.webview.postMessage({ type: 'pageReady' });

function animate(time) {
  controls.animationFrame = requestAnimationFrame(animate);
  const delta = controls.previousTime ? Math.min((time - controls.previousTime) / 1000, 0.1) : 0;
  controls.previousTime = time;
  if (controls.mixer && controls.activeAction) {
    controls.mixer.update(delta);
    render();
  }
  if (controls.orbit) controls.orbit.update(delta);
}
controls.animationFrame = requestAnimationFrame(animate);
