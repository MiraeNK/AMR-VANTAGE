const fs = require('fs');
const path = require('path');
const sharp = require('C:/Users/Husaini/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp');
const out = path.resolve(__dirname, '..');
fs.mkdirSync(out, {recursive:true});
const glyphs = {
 V:'M0 0H29L49 80L70 0H99L64 112H33Z',
 A:'M34 0H66L103 112H75L68 88H31L24 112H-4ZM49 26L37 65H61Z',
 N:'M0 112V0H28L70 69V0H96V112H68L26 43V112Z',
 T:'M0 0H100V25H63V112H36V25H0Z',
 G:'M96 0H28Q0 0 0 28V84Q0 112 28 112H96V52H48V75H70V88H32Q26 88 26 82V30Q26 24 32 24H96Z',
 E:'M0 0H89V24H27V44H80V68H27V88H89V112H0Z',
 F:'M0 0H88V24H27V47H76V71H27V112H0Z',
 M:'M0 112V0H29L54 49L79 0H108V112H82V44L63 81H45L26 44V112Z',
 R:'M0 112V0H61Q94 0 94 31V38Q94 62 73 68L101 112H69L44 72H27V112ZM27 24V49H57Q67 49 67 39V34Q67 24 57 24Z'
};
const blue='#075BCC', navy='#071D39';
function letters(word, fill, x, y, scale=1) {
 let cursor=0;
 const shapes=[...word].map(c=>{
  const el='<path transform="translate('+cursor+' 0)" d="'+glyphs[c]+'"/>';
  cursor+=c==='M'?120:c==='A'?114:109;
  return el;
 }).join('');
 return '<g fill="'+fill+'" fill-rule="evenodd" transform="translate('+x+' '+y+') scale('+scale+')"><g transform="matrix(1 0 -0.2 1 22.4 0)">'+shapes+'</g></g>';
}
function emblem(fill, transform='translate(60 67)') {
 return '<g fill="'+fill+'" transform="'+transform+'"><rect width="56" height="56" rx="6"/><rect y="136" width="56" height="56" rx="6"/><path d="M66 10H166Q172 10 178 16L230 68H266Q274 68 276 76L284 96L276 116Q274 124 266 124H230L178 176Q172 182 166 182H66V146H150Q156 146 162 140L190 112Q196 106 204 106H218L230 96L218 86H204Q196 86 190 80L162 52Q156 46 150 46H66Z"/></g>';
}
function contents(tone='primary') {
 const ink=tone==='white'?'#FFFFFF':tone==='mono'?navy:navy;
 const symbol=tone==='white'?'#FFFFFF':tone==='mono'?navy:blue;
 return emblem(symbol)+letters('FMR',ink,391,62,.38)+letters('VANTAGE',ink,383,146,1);
}
function svg(body,w=1200,h=320,desc='FMR VANTAGE: two robot routes joining a coordinated fleet segment.') {
 return '<svg xmlns="http://www.w3.org/2000/svg" width="'+w+'" height="'+h+'" viewBox="0 0 '+w+' '+h+'" role="img" aria-labelledby="title desc"><title id="title">FMR VANTAGE</title><desc id="desc">'+desc+'</desc>'+body+'</svg>';
}
async function save(name, data, width) {
 fs.writeFileSync(path.join(out,name+'.svg'),data);
 await sharp(Buffer.from(data)).resize({width}).png().toFile(path.join(out,name+'.png'));
}
(async()=>{
 await save('fmr-vantage-primary',svg(contents()),3600);
 await save('fmr-vantage-white',svg(contents('white')),3600);
 await save('fmr-vantage-monochrome',svg(contents('mono')),3600);
 await save('fmr-vantage-symbol',svg(emblem(blue,'translate(50 96)'),384,384),1024);
 const preview = '<rect width="1440" height="1040" fill="#F3F5F8"/>'
 + '<text x="90" y="72" font-family="Arial,sans-serif" font-size="18" font-weight="700" letter-spacing="2" fill="'+navy+'">FMR VANTAGE / LOGO CONCEPT 01</text>'
 + '<rect x="60" y="110" width="1320" height="390" rx="16" fill="white"/>'
 + '<g transform="translate(120 145)">'+contents()+'</g>'
 + '<rect x="60" y="530" width="860" height="360" rx="16" fill="'+navy+'"/>'
 + '<g transform="translate(84 600) scale(.68)">'+contents('white')+'</g>'
 + '<rect x="950" y="530" width="430" height="360" rx="16" fill="white"/>'
 + emblem(blue,'translate(1023 614)')
 + '<text x="60" y="946" font-family="Arial,sans-serif" font-size="21" fill="'+navy+'">Koordinasi jalur. Gerak terarah. Armada terkendali.</text>'
 + '<text x="60" y="984" font-family="Arial,sans-serif" font-size="16" fill="#647084">KONSEP / BELUM DITERAPKAN   -   SVG VEKTOR + PNG TRANSPARAN</text>';
 await sharp(Buffer.from(svg(preview,1440,1040))).png().toFile(path.join(out,'preview.png'));
 for(const name of fs.readdirSync(out).filter(n=>n.endsWith('.png'))){
  const info=await sharp(path.join(out,name)).metadata();
  console.log(JSON.stringify({file:name,width:info.width,height:info.height,alpha:info.hasAlpha}));
 }
})();


