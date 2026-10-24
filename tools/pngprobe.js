/* 零依赖 PNG 探针：解码 PNG（8bit RGB/RGBA/灰度）并按条件统计像素。
 * 用途：截图里 1px 的细节在缩放后肉眼看不见，用它做像素级证据。
 *
 * 用法：
 *   node tools/pngprobe.js <图片> [--min r,g,b] [--rect x,y,w,h]
 * 例：
 *   node tools/pngprobe.js shot.png --min 225,240,245
 *   （找"火流星头部亮核 #f2fbff"这类只有流星才会有的极亮像素）
 */
const fs = require('fs'), zlib = require('zlib');

function decode(file){
  const buf = fs.readFileSync(file);
  if(buf.readUInt32BE(0) !== 0x89504e47) throw new Error('不是 PNG');
  let p = 8, w = 0, h = 0, bd = 0, ct = 0;
  const idat = [];
  while(p < buf.length){
    const len = buf.readUInt32BE(p);
    const type = buf.toString('ascii', p + 4, p + 8);
    const data = buf.slice(p + 8, p + 8 + len);
    if(type === 'IHDR'){ w = data.readUInt32BE(0); h = data.readUInt32BE(4); bd = data[8]; ct = data[9]; }
    else if(type === 'IDAT') idat.push(data);
    else if(type === 'IEND') break;
    p += 12 + len;
  }
  if(bd !== 8) throw new Error('仅支持 8bit，实际 ' + bd);
  const ch = ct === 6 ? 4 : ct === 2 ? 3 : ct === 0 ? 1 : ct === 4 ? 2 : 0;
  if(!ch) throw new Error('不支持的颜色类型 ' + ct);
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * ch;
  const out = Buffer.alloc(h * stride);
  let prev = Buffer.alloc(stride);
  for(let y = 0; y < h; y++){
    const ft = raw[y * (stride + 1)];
    const line = raw.slice(y * (stride + 1) + 1, y * (stride + 1) + 1 + stride);
    const cur = Buffer.alloc(stride);
    for(let i = 0; i < stride; i++){
      const a = i >= ch ? cur[i - ch] : 0, b = prev[i], c = i >= ch ? prev[i - ch] : 0, x = line[i];
      let v;
      switch(ft){
        case 0: v = x; break;
        case 1: v = x + a; break;
        case 2: v = x + b; break;
        case 3: v = x + ((a + b) >> 1); break;
        case 4: {
          const pa = Math.abs(b - c), pb = Math.abs(a - c), pc = Math.abs(a + b - 2 * c);
          v = x + ((pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c));
          break;
        }
        default: throw new Error('未知滤波器 ' + ft);
      }
      cur[i] = v & 255;
    }
    cur.copy(out, y * stride);
    prev = cur;
  }
  return {w, h, ch, data: out};
}

function argNum(name, def){
  const i = process.argv.indexOf(name);
  if(i < 0) return def;
  return process.argv[i + 1].split(',').map(Number);
}

const file = process.argv[2];
if(!file){ console.log('用法: node tools/pngprobe.js <png> [--min r,g,b] [--rect x,y,w,h]'); process.exit(1); }
const min = argNum('--min', [225, 240, 245]);
const rect = argNum('--rect', null);

const img = decode(file);
const {w, h, ch, data} = img;
const [rx, ry, rw, rh] = rect || [0, 0, w, h];
console.log('图片 %dx%d  通道 %d  扫描区域 %d,%d %dx%d  阈值 >= %s', w, h, ch, rx, ry, rw, rh, min.join(','));

let hit = 0, x0 = 1e9, y0 = 1e9, x1 = -1, y1 = -1;
const rows = {};
for(let y = ry; y < Math.min(ry + rh, h); y++){
  for(let x = rx; x < Math.min(rx + rw, w); x++){
    const o = y * w * ch + x * ch;
    if(data[o] >= min[0] && data[o + 1] >= min[1] && data[o + 2] >= min[2]){
      hit++;
      if(x < x0) x0 = x; if(x > x1) x1 = x;
      if(y < y0) y0 = y; if(y > y1) y1 = y;
      const band = y - (y % 40);
      (rows[band] = rows[band] || []).push(x);
    }
  }
}
console.log('命中像素: %d', hit);
if(hit){
  console.log('包围盒: x %d~%d, y %d~%d', x0, x1, y0, y1);
  const bands = Object.keys(rows).map(Number).sort((a, b) => a - b);
  for(const b of bands){
    const xs = rows[b];
    console.log('   y %d~%d : %d 个, x 从 %d 到 %d', b, b + 39, xs.length, Math.min(...xs), Math.max(...xs));
  }
}
