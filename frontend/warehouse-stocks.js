// ===== Helpers =====
const $ = (s, r=document)=>r.querySelector(s);
const $$ = (s, r=document)=>[...r.querySelectorAll(s)];
const fmt = v => (v??0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });

function getApiBase() { return localStorage.getItem('apiBase') || 'https://localhost:7225'; }
function setApiBase(v){ localStorage.setItem('apiBase', v); $('#api-base').value = v; }
$('#api-base').value = getApiBase();
$('#btn-set-api').addEventListener('click', ()=> setApiBase($('#api-base').value.trim() || 'https://localhost:7225'));

const state = {
  page: 1,
  pageSize: 20,
  totalPages: 1,
  totalItems: 0
};

async function fetchJSON(url){
  const r = await fetch(url);
  if (!r.ok) throw new Error(await r.text());
  return r.json();
}

function buildPager(){
  const ul = $('#pager'); ul.innerHTML = '';
  const mk = (p, txt, active=false, disabled=false)=>{
    const li = document.createElement('li');
    li.className = 'page-item' + (active?' active':'') + (disabled?' disabled':'');
    const a = document.createElement('a');
    a.className = 'page-link';
    a.href = '#'; a.textContent = txt;
    a.addEventListener('click', (e)=>{ e.preventDefault(); if(!disabled){ state.page = p; load(); } });
    li.appendChild(a); ul.appendChild(li);
  };
  mk(Math.max(1, state.page-1), '«', false, state.page<=1);
  for(let p=1;p<=state.totalPages;p++){
    if (p===1 || p===state.totalPages || Math.abs(p-state.page)<=2){
      mk(p, String(p), p===state.page);
    } else if (Math.abs(p-state.page)===3){
      const li = document.createElement('li'); li.className='page-item disabled';
      li.innerHTML = '<span class="page-link">…</span>'; ul.appendChild(li);
    }
  }
  mk(Math.min(state.totalPages, state.page+1), '»', false, state.page>=state.totalPages);
}

async function loadWarehouses(){
  const url = getApiBase() + '/api/stocks/warehouses';
  const list = await fetchJSON(url);
  const sel = $('#wh'); sel.innerHTML = '';
  for(const w of list){
    const opt = document.createElement('option');
    opt.value = w.locationID; opt.textContent = `${w.name} (#${w.locationID})`;
    sel.appendChild(opt);
  }
}

async function loadCategories(){
  const url = getApiBase() + '/api/Categories';
  const list = await fetchJSON(url);
  const sel = $('#category');
  for(const c of list){
    const opt = document.createElement('option');
    opt.value = c.categoryID; opt.textContent = c.categoryName;
    sel.appendChild(opt);
  }
}

async function load(){
  const wh = $('#wh').value;
  if (!wh) return;
  const search = encodeURIComponent($('#search').value.trim());
  const cat = $('#category').value;
  const sort = $('#sort').value;
  const onlyAvail = $('#onlyAvail').checked;

  const qs = new URLSearchParams();
  if (search) qs.set('search', search);
  if (cat) qs.set('categoryId', cat);
  if (onlyAvail) qs.set('onlyAvailable', 'true');
  qs.set('page', state.page);
  qs.set('pageSize', state.pageSize);
  qs.set('sort', sort);

  const url = `${getApiBase()}/api/stocks/warehouse/${wh}?${qs.toString()}`;
  const res = await fetchJSON(url);
  state.page = res.page; state.pageSize = res.pageSize;
  state.totalPages = res.totalPages; state.totalItems = res.totalItems;

  const tb = $('#tbody'); tb.innerHTML = '';
  for(const it of res.items){
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><code>${it.sku}</code></td>
      <td>${escapeHtml(it.goodName)}</td>
      <td>${escapeHtml(it.categoryName ?? '')}</td>
      <td class="text-end">${fmt(it.onHand)}</td>
      <td class="text-end">${fmt(it.reserved)}</td>
      <td class="text-end">${fmt(it.inTransit)}</td>
      <td class="text-end fw-semibold">${fmt(it.available)}</td>
    `;
    tb.appendChild(tr);
  }
  $('#resultInfo').textContent = `${state.totalItems.toLocaleString('vi-VN')} mặt hàng · Trang ${state.page}/${state.totalPages}`;
  buildPager();
}

function escapeHtml(s) {
  return String(s ?? '')
    .replaceAll('&','&amp;').replaceAll('<','&lt;')
    .replaceAll('>','&gt;').replaceAll('"','&quot;')
    .replaceAll("'",'&#39;');
}

$('#btn-refresh').addEventListener('click', ()=>{ state.page = 1; load(); });
$('#wh').addEventListener('change', ()=>{ state.page = 1; load(); });
$('#search').addEventListener('keydown', e=>{ if(e.key==='Enter'){ state.page=1; load(); }});
$('#category').addEventListener('change', ()=>{ state.page=1; load(); });
$('#sort').addEventListener('change', ()=>{ state.page=1; load(); });
$('#onlyAvail').addEventListener('change', ()=>{ state.page=1; load(); });

(async function init(){
  try{
    await loadWarehouses();
    await loadCategories();
    await load();
  }catch(err){
    console.error(err);
    alert('Lỗi tải dữ liệu: '+ err);
  }
})();
