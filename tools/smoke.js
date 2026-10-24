/* 时间罗盘 自检脚本（可复用）
 * 用法： node tools/smoke.js [html路径]     默认检查 ../时间罗盘.html
 * 五道关：
 *   [A] 语法检查（vm 编译）
 *   [B] 未声明标识符扫描 —— 专抓"某个回调里引用了不存在的变量"
 *       （历史踩坑：ganzhiDay 缺失导致主循环崩、quitting 缺失导致卡片轮巡失效）
 *   [C] 运行时冒烟 —— DOM 桩跑主循环 + 驱动全部定时器回调
 *       （含 2 分钟卡片轮巡、四角循环与定位合法性校验、环旋转方向与落格角度）
 *   [D] 星空呼吸换位 —— 用可控时钟推进 100 秒，校验星点确实在"最暗瞬间"迁位
 *   [E] 节流断言 —— 屏保模式启动后不得注册常驻 rAF（旧版 60Hz 空转是风扇狂转的根因）
 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const HTML = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.join(__dirname, '..', '时间罗盘.html');
const html = fs.readFileSync(HTML, 'utf8');
const js = (html.match(/<script[^>]*>([\s\S]*?)<\/script>/) || [])[1];
if(!js){ console.error('未找到 <script> 块'); process.exit(1); }

let failed = 0;

/* ============ [A] 语法检查 ============ */
try{ new vm.Script(js, {filename:'时间罗盘.html'}); console.log('[A] 语法检查: OK'); }
catch(e){ console.log('[A] 语法错误:', e.message); process.exit(1); }

/* ============ [B] 未声明标识符扫描 ============ */
function sweep(src0){
  let src = src0
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/\/\/[^\n]*/g, ' ')
    .replace(/'(?:[^'\\\n]|\\.)*'/g, '""')
    .replace(/"(?:[^"\\\n]|\\.)*"/g, '""')
    .replace(/`(?:[^`\\]|\\.)*`/g, '""')
    /* 去正则字面量：必须带"前置上下文"判断，否则除法会被误吃。
       曾踩坑：const ux=-m.vy/vlen*0.65, uy=m.vx/vlen*0.65; 中的 /vlen*0.65, uy=m.vx/
       被当成正则删掉 → uy 的声明丢失 → 误报"未声明标识符"。
       正则字面量只可能出现在运算符/括号/`,`/`=`/`return` 之后。 */
    .replace(/(^|[(,=:[!&|?{};\n]|\breturn\b|\btypeof\b|\bcase\b)\s*\/[^\n/*][^\n]*\/[gimsuy]*/g, '$1 ');

  const declared = new Set();
  const addAll = s => String(s).split(/[,\s]+/).forEach(n => {
    const m = n.match(/^([A-Za-z_$][\w$]*)/); if(m) declared.add(m[1]);
  });
  for(const m of src.matchAll(/\b(?:const|let|var)\s+([^;]*)/g)){
    let depth = 0, cur = '';
    for(const ch of m[1]){
      if('([{'.includes(ch)) depth++;
      else if(')]}'.includes(ch)) depth--;
      if(ch === ',' && depth <= 0){ addAll(cur); cur = ''; } else cur += ch;
    }
    addAll(cur);
  }
  for(const m of src.matchAll(/\b(?:const|let|var)\s*[[{]([^\]}]*)[\]}]/g)) addAll(m[1]);
  for(const m of src.matchAll(/\bfunction\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  for(const m of src.matchAll(/\bclass\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  for(const m of src.matchAll(/function\s*[\w$]*\s*\(([^)]*)\)/g)) addAll(m[1]);
  for(const m of src.matchAll(/\(([^)]*)\)\s*=>/g)) addAll(m[1]);
  for(const m of src.matchAll(/([A-Za-z_$][\w$]*)\s*=>/g)) declared.add(m[1]);
  for(const m of src.matchAll(/catch\s*\(\s*([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);
  for(const m of src.matchAll(/\bfor\s*\(\s*(?:const|let|var)\s+([A-Za-z_$][\w$]*)/g)) declared.add(m[1]);

  const ALLOW = new Set(['document','window','location','navigator','console','performance',
    'innerWidth','innerHeight','requestAnimationFrame','cancelAnimationFrame',
    'setInterval','setTimeout','clearInterval','clearTimeout',
    'Math','Date','JSON','Object','Array','String','Number','Boolean','Promise','Error','RegExp',
    'Symbol','Map','Set','WeakMap','Proxy','Reflect','parseInt','parseFloat','isNaN','isFinite',
    'encodeURIComponent','decodeURIComponent','undefined','NaN','Infinity','globalThis',
    'this','arguments','true','false','null','new','typeof','instanceof','in','of','return',
    'if','else','for','while','do','switch','case','default','break','continue','try','catch',
    'finally','throw','delete','void','yield','await','async','function','class','extends',
    'super','static','get','set','constructor','let','const','var','AudioContext',
    'webkitAudioContext','localStorage','sessionStorage','matchMedia','alert','item','key','value',
    'length','name','type','text','id','x','y','r','d','n','i','j','k','v','g','e','s','t','p','a','b','c','f','h','m','w','o']);

  const suspects = new Map();
  for(const m of src.matchAll(/(^|[^.\w$])([A-Za-z_$][\w$]*)\s*(?=[^:\w$]|$)/g)){
    const name = m[2];
    if(ALLOW.has(name) || declared.has(name)) continue;
    suspects.set(name, (suspects.get(name) || 0) + 1);
  }
  const list = [...suspects.entries()].filter(([n,c]) => c <= 3 && !/^[A-Z][A-Z0-9_]+$/.test(n));
  return list.map(([n,c]) => n+'×'+c);
}

const undeclared = sweep(js);
if(undeclared.length){
  console.log('[B] 疑似未声明标识符:', undeclared.join(', '));
  failed = 1;
}else{
  console.log('[B] 未声明标识符扫描: OK');
}
/* 扫描器自检：注入一个必然未声明的标识符，必须被抓到。
   扫描器的正则一旦被改坏会"静默失效"——本文件就踩过一次（除法被误判成正则字面量），
   所以这里用"注入 → 必检出"做反向验证，避免它悄悄变成一块橡皮图章。 */
const probeSrc = js.replace('function loop(){', 'function loop(){\n  if(__smoke_probe__) return;', 1);
const probeOk = probeSrc !== js && sweep(probeSrc).some(s => s.indexOf('__smoke_probe__') === 0);
console.log('[B] 扫描器自检（注入 __smoke_probe__）:', probeOk ? 'OK' : '✗ 扫描器已失效');
if(!probeOk) failed = 1;

/* ============ 运行时桩 ============ */
const store = {};
const arcLog = [];
function mkEl(id){
  if(id && store[id]) return store[id];
  const e = {
    id:id||'', attrs:{}, style:{}, dataset:{}, children:[],
    setAttribute(k,v){ this.attrs[k]=v; },
    appendChild(c){ this.children.push(c); return c; },
    querySelectorAll(){ return []; }, querySelector(){ return null; },
    classList:{ add(){}, remove(){}, toggle(){} },
    getBoundingClientRect(){ return {width:1000,height:1000,left:0,top:0}; },
    textContent:'', innerHTML:'',
    getContext(kind){ return kind === '2d' ? canvas2d : new Proxy({},{get:()=>()=>{}}); },
    addEventListener(){}, removeEventListener(){}, focus(){}, blur(){}
  };
  if(id) store[id]=e;
  return e;
}
const canvas2d = {
  fillStyle:'', strokeStyle:'', lineWidth:1, globalAlpha:1, lineCap:'butt',
  clearRect(){}, beginPath(){}, fill(){}, moveTo(){}, lineTo(){}, closePath(){}, stroke(){},
  /* 流星的拖尾用线性渐变：桩必须实现，否则整块星空回调会抛错 */
  createLinearGradient(){ return { addColorStop(){} }; },
  getImageData(){ return { data:new Uint8ClampedArray(4) }; },
  arc(x,y,r){ arcLog.push({x,y,r}); }
};
class FakeParam{
  constructor(){ this.value=0; }
  setValueAtTime(){ return this; }
  exponentialRampToValueAtTime(){ return this; }
}
class FakeAC{
  constructor(){ this.currentTime=0; this.destination={}; this.state='running'; }
  resume(){ return Promise.resolve(); }
  createOscillator(){ return { frequency:new FakeParam(), connect(){ return this; }, start(){}, stop(){} }; }
  createGain(){ return { gain:new FakeParam(), connect(){ return this; } }; }
}
/* 可控时钟：让"呼吸换位"这类长周期行为能在毫秒级测试里被观察到 */
let fakeNow = Date.now();
class FakeDate extends Date{
  constructor(...a){ if(a.length === 0){ super(fakeNow); } else { super(...a); } }
  static now(){ return fakeNow; }
}

const intervals = [], timeouts = [], rafQueue = [];
const sandbox = {
  console, Math, JSON, Date:FakeDate, Object, Array, String, Number, Boolean, Promise, Error, RegExp,
  Symbol, Map, Set, parseInt, parseFloat, isNaN, isFinite,
  requestAnimationFrame: cb => { rafQueue.push(cb); return rafQueue.length; },
  cancelAnimationFrame: () => {},
  setInterval: (fn, ms) => { intervals.push({fn, ms}); return intervals.length; },
  setTimeout: (fn, ms) => { timeouts.push({fn, ms}); return timeouts.length; },
  clearInterval(){}, clearTimeout(){},
  document: {
    getElementById: id => mkEl(id),
    createElementNS: () => mkEl(),
    createElement: () => mkEl(),
    querySelectorAll: () => [],
    body: mkEl('__body'),
    documentElement: mkEl('__docel'),
    fullscreenElement: null,
    addEventListener(){}
  },
  window: { addEventListener(){}, AudioContext:FakeAC, webkitAudioContext:FakeAC,
            innerWidth:1920, innerHeight:1080 },
  location: { hash:'#scr', search:'' },      /* 屏保模式，最贴近实际使用 */
  innerWidth: 1920, innerHeight: 1080,
  navigator: { userAgent:'node-smoke' },
  performance: { now: () => fakeNow },
  alert(){}
};
sandbox.globalThis = sandbox;

/* 驱动所有挂起的定时器（主循环与星空都走 setTimeout 了） */
function driveTimers(steps, stepMs){
  for(let i=0;i<steps;i++){
    fakeNow += (stepMs===undefined ? 1000 : stepMs);
    const q = timeouts.slice(); timeouts.length = 0;
    for(const t of q){ t.fn(); }
  }
}

/* ============ [C] 运行时冒烟 ============ */
try{
  vm.createContext(sandbox);
  vm.runInContext(js, sandbox, {filename:'时间罗盘.html'});
}catch(e){
  console.log('[C] 脚本初始化抛错:', e.message); process.exit(1);
}

/* --- 盘面结构：15 层环 --- */
const dial = store['dial'];
if(!dial){ console.log('[C] 未取到 dial 桩'); process.exit(1); }
const beamG = dial.children[0], ringsG = dial.children[1];
const rings = ringsG.children.filter(c => c.attrs && c.attrs.class === 'ring');
let cells = 0;
(function count(n){ for(const c of n){ if(c.attrs && c.attrs.class === 'cell') cells++; count(c.children||[]); } })(rings);
let texts = 0;
(function count(n){ for(const c of n){ if(c.attrs && c.attrs.class === 'ctext') texts++; count(c.children||[]); } })(rings);
console.log('[C] 环层数:', rings.length, '| 格子', cells, '| 文字', texts, '| 固定色块柱', beamG.children.length);
if(rings.length !== 15 || beamG.children.length !== 15){
  console.log('[C] ✗ 环层数或色块柱数量不对'); failed = 1;
}
/* 固定色块柱必须没有 transform（永不转动） */
const beamRotated = beamG.children.some(p => p.attrs && p.attrs.transform);
console.log('[C] 色块柱带 transform（应为 false）:', beamRotated);
if(beamRotated){ console.log('[C] ✗ 色块柱不该带变换'); failed = 1; }

/* --- 主循环跑得起来 --- */
const before = rings.map(g => g.style.transform);
driveTimers(3, 1000);
const after = rings.map(g => g.style.transform);
if(after.some(t => t === undefined)){ console.log('[C] ✗ 有环未落位'); failed = 1; }
console.log('[C] 主循环存活: 3 拍后 15 环均已落位 => OK');

/* --- 落格角度校验：整环旋转必须是索引 × 每格角度 --- */
const secG = rings[14], minG = rings[13];
driveTimers(1, 1000);
const mSec = /rotate\(([-\d.]+)deg\)/.exec(secG.style.transform || '');
if(!mSec){ console.log('[C] ✗ 秒环没有 transform'); failed = 1; }
else{
  const deg = parseFloat(mSec[1]);
  const sec = new Date(fakeNow).getSeconds();
  /* 累计角度只增不减，取模后应等于 秒 × 6° */
  const norm = ((deg % 360) + 360) % 360;
  console.log('[C] 秒环累计角度:', deg+'°', '归一化', norm+'°', '期望', (sec*6)+'°');
  if(norm !== sec*6){ console.log('[C] ✗ 秒环落格角度不符'); failed = 1; }
}
/* 旋转只增不减（不会倒转） */
const d1 = parseFloat((/rotate\(([-\d.]+)deg\)/.exec(secG.style.transform)||[0,0])[1]);
driveTimers(60, 1000);
const d2 = parseFloat((/rotate\(([-\d.]+)deg\)/.exec(secG.style.transform)||[0,0])[1]);
console.log('[C] 60 秒后秒环累计角度:', d1+'° →', d2+'°（应只增不减，且为 360 的整数倍增量）');
if(d2 < d1){ console.log('[C] ✗ 秒环出现倒转'); failed = 1; }
if(((d2 - d1) % 360 + 360) % 360 !== 0){ console.log('[C] ✗ 秒环 60 秒增量不是整圈'); failed = 1; }

/* --- 卡片四角轮巡 --- */
let ok = true;
const card = store['infoCard'];
const insetOk = c => {
  const has = k => c[k] === '16px', auto = k => c[k] === 'auto';
  return ((has('top') && auto('bottom')) || (has('bottom') && auto('top'))) &&
         ((has('left') && auto('right')) || (has('right') && auto('left')));
};
const rot = intervals.find(i => i.ms === 120000);
if(!rot){ console.log('[C] 未注册卡片轮巡定时器（120000ms）—— 轮巡功能缺失'); process.exit(1); }
for(let round=1; round<=4; round++){
  try{ rot.fn(); }catch(e){ console.log('[C] 轮巡回调抛错:', e.message); process.exit(1); }
  /* 必须先快照再清空：主循环会往 timeouts 里追加，边遍历边追加会死循环 */
  const qs = timeouts.slice(); timeouts.length = 0;
  for(const tm of qs){ try{ tm.fn(); }catch(e){ console.log('[C] fade 回调抛错:', e.message); process.exit(1); } }
  const c = card.style;
  const good = insetOk(c) && c.opacity === '0.68';
  if(!good) ok = false;
  console.log('    轮巡 '+round+': top='+JSON.stringify(c.top)+' bottom='+JSON.stringify(c.bottom)+
              ' left='+JSON.stringify(c.left)+' right='+JSON.stringify(c.right)+
              ' opacity='+c.opacity+(good?'  OK':'  ✗ 定位非法'));
}
const closed = card.style.top === '16px' && card.style.right === '16px' && card.style.left === 'auto';
console.log('[C] 四轮后回到右上角（循环闭合）:', closed);
if(!closed) failed = 1;

/* ============ [D] 星空呼吸换位 ============ */
function starSnapshot(){
  arcLog.length = 0;
  driveTimers(1, 33);
  return arcLog.map(a => a.x.toFixed(1)+','+a.y.toFixed(1));
}
const p0 = starSnapshot();
for(let i=0;i<40;i++) driveTimers(1, 2500);   /* 按真实帧步进推进 100 秒 */
const p1 = starSnapshot();
const baseSet = new Set(p0);
const same = p1.filter(p => baseSet.has(p)).length;
const movedPct = p1.length ? Math.round((1 - same/p1.length) * 100) : 0;
console.log('[D] 银河星点数:', p1.length, '（基准 '+p0.length+'）');
console.log('[D] 100 秒后仍在原位的星:', same, '→ 换位比例约 ' + movedPct + '%');
const starOk = p1.length >= 100 && movedPct >= 50;
console.log('[D] 星空呼吸换位:', starOk ? 'OK' : '✗ 星点几乎没动（换位逻辑未生效）');
if(!starOk) failed = 1;

/* ============ [E] 节流断言 ============ */
/* 实测结论：屏保 CPU 与"每秒帧数"几乎严格线性（1Hz≈0.25 核 → 60Hz≈1.0 核），
   所以凡是能产生帧的持续动画都必须显式限速。这里把允许的定时器做成白名单。 */
const mDrift = /balanced:\s*\{[^}]*driftMs:(\d+)/.exec(js);
const DRIFTMS = mDrift ? parseInt(mDrift[1], 10) : 0;
console.log('[E] 启动后挂起的 rAF 数:', rafQueue.length, '（屏保模式应为 0）');
console.log('[E] 注册的 setInterval:', intervals.map(i => i.ms+'ms').join(', ') || '(无)');
const driftIv = intervals.find(i => i.ms === DRIFTMS);
const others  = intervals.filter(i => i.ms !== 120000 && i.ms !== DRIFTMS);
const badInterval = others.some(i => i.ms < 1000);
if(rafQueue.length !== 0){ console.log('[E] ✗ 屏保模式不应注册常驻 rAF（会按刷新率空转）'); failed = 1; }
if(!driftIv){ console.log('[E] ✗ 未注册漂移步进定时器（盘面不动 = 有烧屏风险）'); failed = 1; }
if(DRIFTMS < 33){ console.log('[E] ✗ 漂移帧间隔 '+DRIFTMS+'ms 过快（实测 CPU 与帧率线性相关）'); failed = 1; }
if(badInterval){
  console.log('[E] ✗ 存在未白名单的高频 setInterval:', others.filter(i=>i.ms<1000).map(i=>i.ms).join(','));
  failed = 1;
}
if(rafQueue.length === 0 && !badInterval && driftIv)
  console.log('[E] 节流断言: OK（主循环 1Hz、星空定时驱动、漂移 '+DRIFTMS+'ms 亚像素步进）');

console.log('');
if(ok && closed && !failed){
  console.log('===== 自检全部通过 =====');
  process.exit(0);
}else{
  console.log('===== 自检未通过（见上方 ✗ / 疑点） =====');
  process.exit(1);
}
