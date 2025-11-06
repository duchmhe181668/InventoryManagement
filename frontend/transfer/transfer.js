/* =========================
   Transfer (Store → Warehouse)
   Auth kiểu Purchase: accessToken + role
   ========================= */

const API_BASE = 'https://localhost:7225/api';

// ===== AUTH (đơn giản như purchase) =====
function parseJwt(token) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64).split('').map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)).join('')
    );
    return JSON.parse(jsonPayload);
  } catch { return null; }
}

let AUTH_TOKEN = '';
function checkAuth() {
  const token = localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken');
  if (!token) {
    alert('Bạn chưa đăng nhập hoặc phiên đã hết hạn. Vui lòng đăng nhập lại.');
    window.location.href = '../login.html';
    return false;
  }
  AUTH_TOKEN = token;

  // role
  let userRole = localStorage.getItem('authRole') || sessionStorage.getItem('authRole');
  if (!userRole) {
    const payload = parseJwt(token);
    userRole = payload ? (payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '') : '';
  }
  const role = (userRole || '').toLowerCase();
  const allowed = ['storemanager', 'administrator']; // cho StoreManager & Admin
  if (!allowed.includes(role)) {
    alert('Bạn không có quyền truy cập chức năng này.');
    window.location.href = '../home.html';
    return false;
  }
  return true;
}

// ===== Helpers =====
const el = id => document.getElementById(id);
const esc = s => (s??'').toString().replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const debounce = (fn, ms=300) => { let t; return (...a)=>{ clearTimeout(t); t=setTimeout(()=>fn(...a), ms);} };

function showToast(message, variant='primary', delay=2800){
  const ct = el('toastContainer');
  const div = document.createElement('div');
  const color = ({success:'success', error:'danger', warning:'warning', info:'primary', primary:'primary'})[variant] || 'primary';
  div.className = `toast align-items-center bg-white border border-2 border-${color} text-${color}`;
  div.setAttribute('role','alert'); div.setAttribute('aria-live','assertive'); div.setAttribute('aria-atomic','true');
  div.innerHTML = `<div class="d-flex"><div class="toast-body fw-semibold">${esc(message)}</div><button type="button" class="btn-close me-2 m-auto" data-bs-dismiss="toast"></button></div>`;
  ct.appendChild(div);
  const t = new bootstrap.Toast(div, { delay });
  div.addEventListener('hidden.bs.toast', ()=> div.remove());
  t.show();
}

// ===== State =====
let FROM_ID = null; // warehouse
let TO_ID = null;   // store (lock)
let ITEMS = [];     // {goodId, sku, name, available, qty, batchId?}

let goodBox, goodUl;

// ===== Boot =====
window.addEventListener('DOMContentLoaded', async () => {
  if (!checkAuth()) return;

  // UI refs
  goodBox = el('goodResults'); goodUl = goodBox.querySelector('ul');

  // events
  el('btnClearAll').addEventListener('click', ()=>{ if (ITEMS.length && confirm('Xoá hết mặt hàng?')) { ITEMS=[]; renderItems(); } });
  el('btnDraft').addEventListener('click', ()=> saveOrSubmit(true));
  el('btnSubmit').addEventListener('click', ()=> saveOrSubmit(false));
  el('goodSearch').addEventListener('input', debounce(onSearchGoods, 300));

  // load header
  try { await loadHeader(); } catch (e) { showToast('Không tải được dữ liệu ban đầu', 'error'); }

  renderItems();

  // click outside dropdown
  document.addEventListener('click', (e)=>{
    const input = el('goodSearch');
    if (goodBox && input && !goodBox.contains(e.target) && e.target !== input)
      goodBox.classList.add('d-none');
  });
});

// ===== API helper =====
async function api(path, method='GET', body=null, mustOk=false) {
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      'Authorization': `Bearer ${AUTH_TOKEN}`,
      ...(body ? {'Content-Type':'application/json'} : {})
    },
    body: body ? JSON.stringify(body) : null
  });
  if (!res.ok) {
    if (res.status === 401) { alert('Phiên đăng nhập đã hết hạn.'); location.href='../login.html'; }
    if (res.status === 403) { alert('Bạn không có quyền thực hiện.'); location.href='../home.html'; }
    const txt = await res.text().catch(()=> '');
    if (mustOk) throw new Error(txt || `HTTP ${res.status}`);
  }
  try { return await res.json(); } catch { return null; }
}

// ===== Header (From/To/CreatedBy) =====
async function loadHeader() {
  // warehouses
  const whs = await api('/locations?type=WAREHOUSE&active=true', 'GET', null, true);
  const sel = el('fromLocation');
  sel.innerHTML = (whs||[]).map(x => `<option value="${x.locationID}">${esc(x.name || ('#'+x.locationID))}</option>`).join('');
  FROM_ID = whs?.[0]?.locationID || null;
  sel.addEventListener('change', ()=>{ FROM_ID = Number(sel.value); ITEMS=[]; renderItems(); });

  // profile → store & created by
  const me = await api('/auth/profile', 'GET', null, true);
  TO_ID = me?.storeDefaultLocationId || me?.storeLocationId || null;
  el('toLocation').value = TO_ID || '';
  el('toLocationName').value = me?.storeDefaultLocationName || me?.storeLocationName || (TO_ID ? `#${TO_ID}` : '');
  el('createdByName').value = me?.name || me?.username || '';
}

// ===== Search goods with Available at FROM =====
async function onSearchGoods(e){
  const q = e.target.value.trim();
  if (!q || !FROM_ID) { goodBox.classList.add('d-none'); goodUl.innerHTML=''; return; }
  try {
    // BE: /api/stocks/available?locationId=...&kw=...
    const data = await api(`/stocks/available?locationId=${FROM_ID}&kw=${encodeURIComponent(q)}`, 'GET', null, true);
    goodUl.innerHTML = '';
    (data || []).forEach(g => {
      const li = document.createElement('li');
      li.className = 'list-group-item list-group-item-action';
      li.innerHTML = `
        <div class="d-flex justify-content-between">
          <div>
            <div class="fw-semibold">${esc(g.goodName)}</div>
            <div class="small text-muted">SKU: ${esc(g.sku||'')}</div>
          </div>
          <div class="text-end">
            <div class="small text-muted">Available</div>
            <div class="fw-semibold">${g.available}</div>
          </div>
        </div>`;
      li.addEventListener('click', ()=>{
        const idx = ITEMS.findIndex(x => x.goodId === g.goodID);
        if (idx >= 0) { ITEMS[idx].qty = Math.min(ITEMS[idx].qty + 1, Number(g.available)||999999); }
        else {
          ITEMS.push({
            goodId: g.goodID, sku: g.sku, name: g.goodName,
            available: Number(g.available)||0, qty: 1, batchId: null
          });
        }
        renderItems();
        goodUl.innerHTML=''; goodBox.classList.add('d-none');
        el('goodSearch').value = '';
      });
      goodUl.appendChild(li);
    });
    goodBox.classList.toggle('d-none', !data || data.length===0);
  } catch (err) {
    console.error(err); showToast('Không tải được danh sách hàng', 'error');
  }
}

// ===== Render lines =====
function renderItems(){
  const tb = el('itemsBody');
  if (!ITEMS.length) { tb.innerHTML = `<tr><td colspan="7" class="text-center text-muted">No items</td></tr>`; }
  else {
    tb.innerHTML = ITEMS.map((x, i)=>`
      <tr>
        <td>${i+1}</td>
        <td>${esc(x.sku||'')}</td>
        <td>${esc(x.name||'')}</td>
        <td class="text-center">${x.available}</td>
        <td>
          <input type="number" min="1" step="1" class="form-control form-control-sm qty-input"
                 value="${x.qty}" data-idx="${i}">
        </td>
        <td>
          <input type="number" min="0" step="1" class="form-control form-control-sm"
                 value="${x.batchId??''}" data-batch="${i}">
        </td>
        <td class="text-center">
          <button class="btn btn-sm btn-outline-danger icon-btn" data-del="${i}">
            <i class="fa-solid fa-trash-can fa-sm"></i>
          </button>
        </td>
      </tr>
    `).join('');
  }
  // tổng
  el('totalItems').textContent = String(ITEMS.length);

  // bind inputs
  tb.querySelectorAll('input[data-idx]').forEach(inp=>{
    inp.addEventListener('change', e=>{
      const i = Number(e.target.dataset.idx);
      let v = Number(e.target.value);
      if (!Number.isFinite(v) || v < 1) v = 1;
      if (v > ITEMS[i].available) showToast(`Vượt Available (${ITEMS[i].available}).`, 'warning');
      ITEMS[i].qty = v; e.target.value = v;
    });
  });
  tb.querySelectorAll('input[data-batch]').forEach(inp=>{
    inp.addEventListener('change', e=>{
      const i = Number(e.target.dataset.batch);
      const v = e.target.value.trim();
      ITEMS[i].batchId = v ? Number(v) : null;
    });
  });
  tb.querySelectorAll('button[data-del]').forEach(btn=>{
    btn.addEventListener('click', e=>{
      const i = Number(e.currentTarget.dataset.del);
      ITEMS.splice(i,1); renderItems();
    });
  });
}

// ===== Save Draft / Submit =====
function validateBeforeSave(){
  if (!FROM_ID) { showToast('Chọn From (Warehouse).','warning'); return false; }
  if (!TO_ID)   { showToast('Không xác định được Store.','warning'); return false; }
  if (!ITEMS.length) { showToast('Chưa có mặt hàng.','warning'); return false; }
  if (ITEMS.some(x => !x.goodId || !x.qty || x.qty < 1)) { showToast('Số lượng không hợp lệ.','warning'); return false; }
  return true;
}

async function saveOrSubmit(isDraft){
  if (!validateBeforeSave()) return;

  try {
    // 1) Tạo draft
    const body = {
      fromLocationId: FROM_ID,
      toLocationId: TO_ID,
      items: ITEMS.map(x => ({ goodId: x.goodId, quantity: x.qty, batchId: x.batchId }))
    };
    const created = await api('/transfers', 'POST', body, true);
    const tid = created?.transferID || created?.TransferID || created?.id;
    if (!tid) throw new Error('Không tạo được phiếu.');

    // 2) Nếu submit: gọi /submit
    if (!isDraft) {
      await api(`/transfers/${tid}/submit`, 'POST', null, true);
      showToast('Đã gửi yêu cầu transfer.', 'success');
    } else {
      showToast('Đã lưu nháp transfer.', 'success');
    }

    setTimeout(()=> location.href = '../transfer_list/transfer_list.html', 800);
  } catch (e) {
    console.error(e);
    showToast(`Lỗi: ${e.message}`, 'error');
  }
}
