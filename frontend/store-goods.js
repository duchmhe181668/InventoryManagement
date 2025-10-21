// ===== Helpers (no-auth) =====
const $ = (s, r=document)=>r.querySelector(s);
const $$ = (s, r=document)=>[...r.querySelectorAll(s)];
const fmt = v => (v??0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });

function getApiBase() {
  return localStorage.getItem('apiBase') || 'https://localhost:7225';
}
function setApiBase(v){
  localStorage.setItem('apiBase', v);
  $('#api-base').value = v;
}
$('#api-base').value = getApiBase();
$('#btn-set-api').addEventListener('click', ()=> setApiBase($('#api-base').value.trim() || 'https://localhost:7225'));
$('#btn-ping').addEventListener('click', async ()=>{
  const url = getApiBase() + '/api/Stores/1/goods';
  try{
    const r = await fetch(url);
    alert(r.ok ? 'Ping OK: ' + url : 'Ping FAIL: ' + r.status);
  }catch(e){ alert('Ping error: ' + e.message); }
});

// ===== State =====
const state = { page:1, pageSize:20, sort:'name', search:'' };
let storeId = Number(localStorage.getItem('storeId')||'');
if(!storeId) storeId = null;

// ===== DOM =====
const tbody = $('#tbody');
const searchInput = $('#search-input');
const btnPrev = $('#btn-prev'), btnNext = $('#btn-next'), pageInfo = $('#page-info');
const mainMsg = $('#mainMsg');
const storeIdInput = $('#store-id'); const btnSetStore = $('#btn-set-store'); const locInfo = $('#loc-info');

// Modal
const priceModal = new bootstrap.Modal('#priceModal');
const priceForm = $('#priceForm'); const priceMsg = $('#priceMsg');
let editingGoodId = null;

// ===== API URLs =====
const API_STORES_GOODS = (sid)=> getApiBase() + `/api/Stores/${sid}/goods`;
const API_STOREPRICE   = ()=> getApiBase() + '/api/StorePrices/current';
const API_STORE_LOC    = (sid)=> getApiBase() + `/api/Stores/store-location/${sid}`;

// ===== Store set/get =====
function refreshStoreUI(){
  storeIdInput.value = storeId ?? '';
  locInfo.textContent = '';
  if(storeId) fetchLocationInfo(storeId);
}
async function fetchLocationInfo(sid){
  try{
    const res = await fetch(API_STORE_LOC(sid));
    if(!res.ok){ locInfo.textContent = '(không lấy được Location)'; return; }
    const data = await res.json();
    const locId = data.locationId ?? data.LocationID ?? data.locationID ?? '';
    locInfo.textContent = locId ? `LocationID: ${locId}` : '';
  }catch{ /* ignore */ }
}
btnSetStore.addEventListener('click', ()=>{
  const val = Number(storeIdInput.value||'0');
  if(!val){ alert('Nhập Store ID hợp lệ'); return; }
  storeId = val;
  localStorage.setItem('storeId', String(storeId));
  refreshStoreUI();
  fetchGoods();
});

// ===== Fetch Goods for store =====
async function fetchGoods(){
  mainMsg.textContent = '';
  if(!storeId){
    tbody.innerHTML = `<tr><td colspan="10" class="text-center py-5">Nhập <b>Store ID</b> rồi bấm <b>Set</b>.</td></tr>`;
    pageInfo.textContent = '—';
    return;
  }
  const url = new URL(API_STORES_GOODS(storeId));
  url.searchParams.set('page', state.page);
  url.searchParams.set('pageSize', state.pageSize);
  url.searchParams.set('sort', state.sort);
  if(state.search) url.searchParams.set('search', state.search);

  try{
    const res = await fetch(url);
    if(!res.ok) throw new Error('HTTP '+res.status);
    const data = await res.json();
    const items = data.items || data; // fallback nếu controller trả array
    renderRows(items);
    renderPager(data);
  }catch(err){
    tbody.innerHTML = `<tr><td colspan="10" class="text-danger text-center py-4">Không tải được danh sách (${err.message})</td></tr>`;
    pageInfo.textContent = '—';
  }
}

function norm(x){
  return {
    goodID: x.goodID ?? x.GoodID ?? x.id ?? x.Id,
    name: x.name ?? x.Name ?? '',
    sku: x.sku ?? x.SKU ?? '',
    barcode: x.barcode ?? x.Barcode ?? '',
    unit: x.unit ?? x.Unit ?? '',
    imageURL: x.imageURL ?? x.imageUrl ?? x.ImageURL ?? '',
    categoryName: x.categoryName ?? x.CategoryName ?? '',
    onHand: x.onHand ?? x.OnHand ?? 0,
    reserved: x.reserved ?? x.Reserved ?? 0,
    inTransit: x.inTransit ?? x.InTransit ?? 0,
    available: x.available ?? x.Available ?? 0,
    priceSell: x.priceSell ?? x.PriceSell ?? 0
  };
}
function renderRows(items){
  tbody.innerHTML = '';
  (items||[]).map(norm).forEach(g=>{
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${g.imageURL?`<img class="img-40" src="${g.imageURL}">`:'<div class="img-40"></div>'}</td>
      <td><div class="truncate">${g.name}</div></td>
      <td><span class="badge bg-light text-dark">${g.sku}</span></td>
      <td>${g.categoryName}</td>
      <td class="text-end">${fmt(g.onHand)}</td>
      <td class="text-end">${fmt(g.reserved)}</td>
      <td class="text-end">${fmt(g.inTransit)}</td>
      <td class="text-end ${(+g.available)<=0?'text-danger-strong':''}">${fmt(g.available)}</td>
      <td class="text-end">${fmt(g.priceSell)}</td>
      <td class="text-end">
        <button class="btn btn-sm btn-outline-primary" data-act="price" data-id="${g.goodID}" data-price="${g.priceSell}">
          <i class="fa-solid fa-pen-to-square"></i>
        </button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}
function renderPager(data){
  const total = data.total ?? data.Total ?? 0;
  const pageCount = data.pageCount ?? data.PageCount ?? 1;
  const page = data.page ?? data.Page ?? 1;
  pageInfo.textContent = `Trang ${page}/${pageCount} — ${total} mục`;
  btnPrev.disabled = page<=1; btnNext.disabled = page>=pageCount;
}

// Sort/Search/Paging
$$('.sortable').forEach(th=> th.addEventListener('click', ()=>{ state.sort = th.dataset.sort; fetchGoods(); }));
searchInput.addEventListener('keypress', e=>{ if(e.key==='Enter'){ state.search=searchInput.value.trim(); state.page=1; fetchGoods(); }});
btnPrev.addEventListener('click', ()=>{ if(state.page>1){ state.page--; fetchGoods(); }});
btnNext.addEventListener('click', ()=>{ state.page++; fetchGoods(); });

// ===== Edit price (StorePrices) =====
tbody.addEventListener('click', (e)=>{
  const btn = e.target.closest('button'); if(!btn) return;
  if(btn.dataset.act==='price'){
    editingGoodId = +btn.dataset.id;
    priceForm.price.value = btn.dataset.price || 0;
    priceForm.effectiveFrom.value = '';
    priceMsg.textContent = '';
    priceModal.show();
  }
});
priceForm.addEventListener('submit', async (e)=>{
  e.preventDefault();
  if(!storeId || !editingGoodId) return;
  const body = {
    storeId,
    goodId: editingGoodId,
    priceSell: Number(priceForm.price.value||0)
  };
  const eff = priceForm.effectiveFrom.value;
  if(eff) body.effectiveFrom = eff;

  priceMsg.textContent = 'Đang lưu...';
  try{
    const res = await fetch(API_STOREPRICE(), {
      method:'PUT', headers:{ 'Content-Type':'application/json' }, body: JSON.stringify(body)
    });
    if(res.ok){
      priceModal.hide();
      fetchGoods();
    }else{
      priceMsg.textContent = 'Không lưu được giá.';
    }
  }catch{ priceMsg.textContent = 'Lỗi kết nối.'; }
});

// Init
(function init(){
  // khởi tạo giá trị hiển thị
  $('#store-id').value = localStorage.getItem('storeId') || '';
  refreshStoreUI();
  fetchGoods();
})();
