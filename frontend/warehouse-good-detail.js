// ===== Helpers =====
const $ = (s, r=document)=>r.querySelector(s);
const fmt = v => (v??0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });

function getApiBase(){ return localStorage.getItem('apiBase') || 'https://localhost:7225'; }
function setApiBase(v){ localStorage.setItem('apiBase', v); $('#api-base').value = v; }
$('#api-base').value = getApiBase();
$('#btn-set-api').addEventListener('click', ()=> setApiBase($('#api-base').value.trim() || 'https://localhost:7225'));

const API_GOODS = ()=> getApiBase() + '/api/Goods';
const API_CATS  = ()=> getApiBase() + '/api/Categories';

// ===== Read ID =====
const params = new URLSearchParams(location.search);
const goodId = +(params.get('id')||0);

const el = {
  mainMsg: $('#mainMsg'),
  name: $('#goodName'),
  sku: $('#goodSKU'),
  barcode: $('#goodBarcode'),
  unit: $('#goodUnit'),
  cat: $('#goodCategory'),
  cost: $('#goodCost'),
  sell: $('#goodSell'),
  img:  $('#goodImage'),
  editSection: $('#editSection'),
  editForm: $('#editForm'),
  editMsg: $('#editMsg'),
  btnToggle: $('#btn-edit-toggle'),
  btnDelete: $('#btn-delete'),
  catSelect: $('#catSelect'),
};

async function loadCategories(selectedId){
  try{
    const res = await fetch(API_CATS());
    const cats = await res.json();
    el.catSelect.innerHTML = `<option value="">(Không)</option>` + (cats||[]).map(c=>{
      const id = c.categoryID ?? c.CategoryID;
      const name = c.categoryName ?? c.CategoryName ?? '';
      return `<option value="${id}">${name}</option>`;
    }).join('');
    if(selectedId) el.catSelect.value = selectedId;
  }catch{}
}

async function loadDetail(){
  el.mainMsg.textContent = '';
  if(!goodId){ el.mainMsg.textContent = 'Thiếu id hàng hóa'; return; }
  try{
    const res = await fetch(`${API_GOODS()}/${goodId}`);
    if(!res.ok) throw new Error('Không tải được chi tiết');
    const g = await res.json();
    const id = g.goodID ?? g.GoodID ?? goodId;
    el.name.textContent = g.name ?? g.Name ?? '—';
    el.sku.textContent = g.sku ?? g.SKU ?? '—';
    el.barcode.textContent = g.barcode ?? g.Barcode ?? '—';
    el.unit.textContent = g.unit ?? g.Unit ?? '—';
    el.cat.textContent = g.categoryName ?? g.CategoryName ?? '—';
    el.cost.textContent = fmt(g.priceCost ?? g.PriceCost ?? 0);
    el.sell.textContent = fmt(g.priceSell ?? g.PriceSell ?? 0);
    const imageURL = g.imageURL ?? g.imageUrl ?? g.ImageURL ?? '';
    if(imageURL) el.img.src = imageURL; else el.img.style.display='none';

    // Prefill edit form
    el.editForm.name.value = g.name ?? g.Name ?? '';
    el.editForm.sku.value = g.sku ?? g.SKU ?? '';
    el.editForm.barcode.value = g.barcode ?? g.Barcode ?? '';
    el.editForm.unit.value = g.unit ?? g.Unit ?? '';
    el.editForm.imageURL.value = imageURL || '';
    await loadCategories(g.categoryID ?? g.CategoryID ?? '');
    el.editForm.priceCost.value = (g.priceCost ?? g.PriceCost ?? 0);
    el.editForm.priceSell.value = (g.priceSell ?? g.PriceSell ?? 0);
  }catch(e){
    el.mainMsg.textContent = e.message;
  }
}

el.btnToggle.addEventListener('click', ()=>{
  el.editSection.classList.toggle('d-none');
});

el.btnDelete.addEventListener('click', async ()=>{
  if(!confirm('Xóa hàng hóa này?')) return;
  const res = await fetch(`${API_GOODS()}/${goodId}`, { method:'DELETE' });
  if(res.ok) window.location.href = 'warehouse-goods.html';
  else alert('Không xóa được');
});

el.editForm.addEventListener('submit', async (e)=>{
  e.preventDefault();
  el.editMsg.textContent = 'Đang lưu...';
  const body = {
    name: el.editForm.name.value.trim(),
    sku: el.editForm.sku.value.trim(),
    barcode: (el.editForm.barcode.value||'').trim()||null,
    unit: (el.editForm.unit.value||'').trim()||null,
    imageURL: (el.editForm.imageURL.value||'').trim()||null,
    categoryID: el.editForm.categoryID.value ? +el.editForm.categoryID.value : null,
    priceCost: Number(el.editForm.priceCost.value||0),
    priceSell: Number(el.editForm.priceSell.value||0),
  };
  try{
    const res = await fetch(`${API_GOODS()}/${goodId}`, {
      method:'PUT',
      headers:{ 'Content-Type':'application/json' },
      body: JSON.stringify(body)
    });
    if(!res.ok) throw new Error('Không lưu được');
    el.editMsg.textContent = '';
    await loadDetail();
    el.editSection.classList.add('d-none');
  }catch(err){
    el.editMsg.textContent = err.message;
  }
});

$('#btn-cancel-edit').addEventListener('click', ()=> el.editSection.classList.add('d-none'));

loadDetail();
