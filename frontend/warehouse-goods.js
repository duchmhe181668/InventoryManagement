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
  const url = getApiBase() + '/api/Goods';
  try{
    const r = await fetch(url);
    alert(r.ok ? 'Ping OK: ' + url : 'Ping FAIL: ' + r.status);
  }catch(e){ alert('Ping error: ' + e.message); }
});

// ===== State =====
const state = { page:1, pageSize:20, sort:'name', search:'' };

// ===== DOM =====
const tbody = $('#tbody');
const searchInput = $('#search-input');
const btnPrev = $('#btn-prev'), btnNext = $('#btn-next'), pageInfo = $('#page-info');
const mainMsg = $('#mainMsg');

// Modals
const goodModal = new bootstrap.Modal('#goodModal');
const goodForm = $('#goodForm'); const goodMsg = $('#goodMsg');
const catModal = new bootstrap.Modal('#catModal');
const catList = $('#catList'); const catMsg = $('#catMsg');

// ===== API URLs =====
const API_GOODS = ()=> getApiBase() + '/api/Goods';
const API_CATS  = ()=> getApiBase() + '/api/Categories';

// ===== Goods list =====
async function fetchGoods(){
  mainMsg.textContent = '';
  const url = new URL(API_GOODS());
  url.searchParams.set('page', state.page);
  url.searchParams.set('pageSize', state.pageSize);
  url.searchParams.set('sort', state.sort);
  if(state.search) url.searchParams.set('search', state.search);

  try{
    const res = await fetch(url);
    if(!res.ok) throw new Error('HTTP '+res.status);
    const data = await res.json();
    const items = data.items || data;   // hỗ trợ cả array & object
    renderRows(items);
    renderPager(data);
  }catch(err){
    tbody.innerHTML = `<tr><td colspan="9" class="text-danger text-center py-4">Không tải được hàng (${err.message})</td></tr>`;
    pageInfo.textContent = '—';
  }
}

function renderRows(items){
  tbody.innerHTML = '';
  (items||[]).forEach(g=>{
    const id = g.goodID ?? g.GoodID ?? g.id ?? g.Id;
    const name = g.name ?? g.Name ?? '';
    const sku = g.sku ?? g.SKU ?? '';
    const barcode = g.barcode ?? g.Barcode ?? '';
    const unit = g.unit ?? g.Unit ?? '';
    const imageURL = g.imageURL ?? g.imageUrl ?? g.ImageURL ?? '';
    const categoryName = g.categoryName ?? g.CategoryName ?? '';
    const priceCost = g.priceCost ?? g.PriceCost ?? 0;
    const priceSell = g.priceSell ?? g.PriceSell ?? 0;

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>${imageURL?`<img class="img-40" src="${imageURL}">`:'<div class="img-40"></div>'}</td>
      <td><div class="truncate">${name}</div></td>
      <td><span class="badge bg-light text-dark">${sku}</span></td>
      <td>${barcode}</td>
      <td>${unit}</td>
      <td>${categoryName}</td>
      <td class="text-end">${fmt(priceCost)}</td>
      <td class="text-end">${fmt(priceSell)}</td>
      <td class="text-end">
        <button class="btn btn-sm btn-outline-secondary me-1" data-act="detail" data-id="${id}"><i class="fa-solid fa-eye"></i> Xem chi tiết</button>
        <button class="btn btn-sm btn-outline-primary me-1" data-act="edit" data-id="${id}"><i class="fa-solid fa-pen"></i></button>
        <button class="btn btn-sm btn-outline-danger" data-act="del" data-id="${id}"><i class="fa-solid fa-trash"></i></button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function renderPager(data){
  const total = data.totalItems ?? data.TotalItems ?? data.total ?? data.Total ?? 0;
  const pageCount = data.totalPages ?? data.TotalPages ?? data.pageCount ?? data.PageCount ?? 1;
  const page = data.page ?? data.Page ?? 1;
  pageInfo.textContent = `Trang ${page}/${pageCount} — ${total} mục`;
  btnPrev.disabled = page<=1; btnNext.disabled = page>=pageCount;
}

// Sort/Search/Paging
$$('.sortable').forEach(th=> th.addEventListener('click', ()=>{ state.sort = th.dataset.sort; fetchGoods(); }));
searchInput.addEventListener('keypress', e=>{ if(e.key==='Enter'){ state.search=searchInput.value.trim(); state.page=1; fetchGoods(); }});
btnPrev.addEventListener('click', ()=>{ if(state.page>1){ state.page--; fetchGoods(); }});
btnNext.addEventListener('click', ()=>{ const m = /\/(\d+)/.exec(pageInfo.textContent); const pageCount = m ? Number(m[1]) : 1; if(state.page < pageCount){ state.page++; fetchGoods(); }});

// ===== Categories load/list =====
async function loadCategories(selectedId){
  try{
    const res = await fetch(API_CATS());
    const cats = await res.json();
    const sel = $('#catSelect');
    sel.innerHTML = `<option value="">(Không)</option>` + (cats||[]).map(c=>`<option value="${c.categoryID??c.CategoryID}">${c.categoryName??c.CategoryName}</option>`).join('');
    if(selectedId) sel.value = selectedId;
  }catch{ /* ignore */ }
}

$('#btn-manage-cats').addEventListener('click', async ()=>{
  await refreshCatList();
  catModal.show();
});

async function refreshCatList(){
  catMsg.textContent = '';
  try{
    const res = await fetch(API_CATS());
    if(!res.ok) throw 0;
    const cats = await res.json();
    catList.innerHTML = (cats||[]).map(c=>{
      const id = c.categoryID ?? c.CategoryID;
      const name = c.categoryName ?? c.CategoryName ?? '';
      return `<li class="list-group-item d-flex justify-content-between align-items-center">
        <span>${name}</span>
        <div class="btn-group btn-group-sm">
          <button class="btn btn-outline-primary" data-cat-edit="${id}" data-name="${name}"><i class="fa fa-pen"></i></button>
          <button class="btn btn-outline-danger" data-cat-del="${id}"><i class="fa fa-trash"></i></button>
        </div>
      </li>`;
    }).join('');
  }catch{ catMsg.textContent = 'Không tải được danh mục'; }
}

$('#btnCatAdd').addEventListener('click', async ()=>{
  const name = ($('#catName').value||'').trim();
  if(!name){ catMsg.textContent = 'Nhập tên danh mục'; return; }
  const res = await fetch(API_CATS(), {
    method:'POST', headers:{ 'Content-Type':'application/json' },
    body: JSON.stringify({ categoryName: name })
  });
  if(res.ok){ $('#catName').value=''; await refreshCatList(); await loadCategories(); }
  else catMsg.textContent = 'Không thêm được danh mục';
});

catList.addEventListener('click', async (e)=>{
  const btn = e.target.closest('button'); if(!btn) return;
  if(btn.dataset.catEdit){
    const id = +btn.dataset.catEdit;
    const old = btn.dataset.name;
    const name = prompt('Tên mới', old||''); if(name==null) return;
    const res = await fetch(`${API_CATS()}/${id}`, {
      method:'PUT', headers:{ 'Content-Type':'application/json' },
      body: JSON.stringify({ categoryID:id, categoryName:name })
    });
    if(res.ok){ await refreshCatList(); await loadCategories(); } else catMsg.textContent='Không cập nhật được';
  }
  if(btn.dataset.catDel){
    const id = +btn.dataset.catDel;
    if(!confirm('Xoá danh mục?')) return;
    const res = await fetch(`${API_CATS()}/${id}`, { method:'DELETE' });
    if(res.ok){ await refreshCatList(); await loadCategories(); } else catMsg.textContent='Không xoá được';
  }
});

// ===== Good add/edit/delete =====
let editingId = null;

$('#btn-add').addEventListener('click', async ()=>{
  editingId = null; goodMsg.textContent = '';
  goodForm.reset();
  await loadCategories();
  goodModal.show();
});

tbody.addEventListener('click', async (e)=>{
  const btn = e.target.closest('button'); if(!btn) return;
  const id = +btn.dataset.id;
  if(btn.dataset.act==='detail'){
    window.location.href = 'warehouse-good-detail.html?id=' + id;
    return;
  }

  if(btn.dataset.act==='edit'){
    goodMsg.textContent = '';
    const res = await fetch(`${API_GOODS()}/${id}`);
    if(!res.ok) return alert('Không tải được sản phẩm');
    const g = await res.json();
    editingId = g.goodID ?? g.GoodID ?? id;
    goodForm.name.value = g.name ?? g.Name ?? '';
    goodForm.sku.value = g.sku ?? g.SKU ?? '';
    goodForm.barcode.value = g.barcode ?? g.Barcode ?? '';
    goodForm.unit.value = g.unit ?? g.Unit ?? '';
    goodForm.imageURL.value = g.imageURL ?? g.imageUrl ?? '';
    goodForm.priceCost.value = g.priceCost ?? g.PriceCost ?? 0;
    goodForm.priceSell.value = g.priceSell ?? g.PriceSell ?? 0;
    await loadCategories(g.categoryID ?? g.CategoryID ?? '');
    goodModal.show();
  }
  if(btn.dataset.act==='del'){
    if(!confirm('Xoá sản phẩm này?')) return;
    const res = await fetch(`${API_GOODS()}/${id}`, { method:'DELETE' });
    if(res.ok) fetchGoods(); else alert('Không xoá được');
  }
});

goodForm.addEventListener('submit', async (e)=>{
  e.preventDefault();
  goodMsg.textContent = 'Đang lưu...';
  const body = {
    name: goodForm.name.value.trim(),
    sku: goodForm.sku.value.trim(),
    barcode: (goodForm.barcode.value||'').trim()||null,
    unit: (goodForm.unit.value||'').trim()||null,
    imageURL: (goodForm.imageURL.value||'').trim()||null,
    categoryID: goodForm.categoryID.value ? +goodForm.categoryID.value : null,
    priceCost: Number(goodForm.priceCost.value||0),
    priceSell: Number(goodForm.priceSell.value||0)
  };
  let res;
  if(editingId){
    body.goodID = editingId;
    res = await fetch(`${API_GOODS()}/${editingId}`, {
      method:'PUT', headers:{ 'Content-Type':'application/json' }, body: JSON.stringify(body)
    });
  }else{
    res = await fetch(API_GOODS(), {
      method:'POST', headers:{ 'Content-Type':'application/json' }, body: JSON.stringify(body)
    });
  }
  if(res.ok){ goodModal.hide(); fetchGoods(); }
  else goodMsg.textContent = 'Không lưu được (trùng SKU/barcode hoặc thiếu dữ liệu?)';
});

// Init
fetchGoods();
