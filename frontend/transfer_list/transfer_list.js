/* =========================
   Transfer (Store → Warehouse)
   Auth pattern: Giống Purchase Order (accessToken + role)
   ========================= */

// ====== Config ======
const API_BASE = 'https://localhost:7225/api';

// ====== Auth helpers (copy phong cách purchase order) ======
function parseJwt(token) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64).split('').map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)).join('')
    );
    return JSON.parse(jsonPayload);
  } catch (e) { return null; }
}

function checkAuth() {
  const token = localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken');
  if (!token) {
    alert("Bạn chưa đăng nhập hoặc phiên đã hết hạn. Vui lòng đăng nhập lại.");
    window.location.href = '../login.html';
    return { token: null, hasPermission: false, role: null };
  }
  let userRole = localStorage.getItem('authRole') || sessionStorage.getItem('authRole');
  if (!userRole) {
    const payload = parseJwt(token);
    // claim role tùy hệ thống, phổ biến: http://schemas.microsoft.com/ws/2008/06/identity/claims/role
    userRole = payload ? (payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '') : '';
  }
  const roleToCompare = (userRole || '').toLowerCase();

  // Trang Transfer (tạo phiếu nhập từ WH về Store) → cho StoreManager & Administrator
  const allowedRoles = ['storemanager', 'administrator'];
  if (!allowedRoles.includes(roleToCompare)) {
    alert("Bạn không có quyền truy cập chức năng này.");
    window.location.href = '../home.html';
    return { token, hasPermission: false, role: roleToCompare };
  }
  return { token, hasPermission: true, role: roleToCompare };
}

// ====== Tiny utils ======
const el = id => document.getElementById(id);
const esc = s => (s ?? '').toString().replace(/[&<>"'\\]/g, m => ({
  '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;','\\':'&#92;'
}[m]));

function showAlert(type, message) {
  const box = el('alertBox');
  const id = 'al' + Math.random().toString(36).slice(2);
  box.innerHTML = `
    <div id="${id}" class="alert alert-${type} alert-dismissible fade show" role="alert">
      ${esc(message)}
      <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>`;
  if (type !== 'danger') {
    setTimeout(() => { const node = document.getElementById(id); if (node) node.classList.remove('show'); }, 4000);
  }
}

function setBusy(flag, which){
  const btnSave = el('btnSaveDraft');
  const btnSubmit = el('btnSubmit');
  const spSave = el('spSave');
  const spSubmit = el('spSubmit');
  if (which === 'save') { btnSave.disabled = flag; spSave?.classList.toggle('d-none', !flag); }
  else if (which === 'submit') { btnSubmit.disabled = flag; spSubmit?.classList.toggle('d-none', !flag); }
  else { btnSave.disabled = btnSubmit.disabled = flag; }
}

// ====== State ======
let TOKEN = null;
let FROM_ID = null; // Warehouse
let TO_ID = null;   // Store (locked from profile)
let LINES = [];     // {goodId, sku, name, available, qty, batchId?}
let ocSearch = null;
let searchTimer = null;

// ====== Boot ======
window.addEventListener('DOMContentLoaded', init);

async function init() {
  // 1) Auth + permission (giống purchase order)
  const auth = checkAuth();
  if (!auth.hasPermission) return;
  TOKEN = auth.token;

  // 2) UI hooks
  ocSearch = new bootstrap.Offcanvas('#ocSearch');
  hookButtons();
  wireSearch();
  renderLines();

  // 3) Load header (From/To/CreatedBy)
  try {
    await loadHeader();
  } catch (e) {
    console.error(e);
    showAlert('danger', 'Không tải được dữ liệu ban đầu: ' + e.message);
  }
}

// ====== API helper (giống purchase order: luôn gắn Authorization) ======
async function api(path, method='GET', body=null, mustOkJson=false) {
  const resp = await fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      'Authorization': `Bearer ${TOKEN}`,
      ...(body ? {'Content-Type': 'application/json'} : {})
    },
    body: body ? JSON.stringify(body) : null
  });
  if (!resp.ok) {
    if (resp.status === 401 || resp.status === 403) {
      // đồng bộ với purchase order: gọi lại checkAuth() để báo & điều hướng
      checkAuth();
    }
    let errText = 'HTTP ' + resp.status;
    try {
      const j = await resp.json();
      errText = j?.message || j?.error || errText;
    } catch {}
    if (mustOkJson) throw new Error(errText);
  }
  try { return await resp.json(); } catch { return null; }
}

// ====== Header (From/To/CreatedBy) ======
async function loadHeader() {
  // From (WAREHOUSE)
  const whs = await api(`/locations?type=WAREHOUSE&active=true`, 'GET', null, true);
  const sel = el('fromLocation');
  sel.innerHTML = (whs || []).map(x => `<option value="${x.locationID}">${esc(x.name || ('#'+x.locationID))}</option>`).join('');
  FROM_ID = whs?.[0]?.locationID || null;
  sel.addEventListener('change', () => { FROM_ID = Number(sel.value); LINES = []; renderLines(); });

  // To (STORE) & CreatedBy
  const me = await api(`/auth/profile`, 'GET', null, true);
  TO_ID = me?.storeDefaultLocationId || me?.storeLocationId || null;
  el('toLocation').value = TO_ID || '';
  el('toLocationName').value = me?.storeDefaultLocationName || me?.storeLocationName || (TO_ID ? `#${TO_ID}` : '');
  el('createdByName').value = me?.name || me?.username || 'Current User';
}

// ====== Search offcanvas ======
function hookButtons() {
  el('btnFindGood').addEventListener('click', () => {
    if (!FROM_ID) { showAlert('warning', 'Chọn From (Warehouse) trước.'); return; }
    el('kw').value = '';
    el('searchList').innerHTML = '';
    ocSearch.show();
    el('kw').focus();
  });

  el('btnSaveDraft').addEventListener('click', saveDraft);
  el('btnSubmit').addEventListener('click', submitTransfer);
}

function wireSearch() {
  el('kw').addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(loadSearch, 300);
  });
}

async function loadSearch() {
  const kw = el('kw').value.trim();
  const list = el('searchList');
  if (!kw || !FROM_ID) { list.innerHTML = ''; return; }

  try {
    const data = await api(`/stocks/available?locationId=${FROM_ID}&kw=${encodeURIComponent(kw)}`, 'GET', null, true);
    if (!Array.isArray(data) || data.length === 0) {
      list.innerHTML = `<div class="list-group-item text-muted">Không có kết quả.</div>`;
      return;
    }
    list.innerHTML = data.map(g => `
      <button type="button" class="list-group-item list-group-item-action"
              onclick='addGood(${g.goodID},"${esc(g.sku)}","${esc(g.goodName)}",${g.available})'>
        <div class="d-flex justify-content-between">
          <div>
            <div><strong>${esc(g.goodName)}</strong></div>
            <div class="small text-muted">SKU: ${esc(g.sku)} • Unit: ${esc(g.unit || '')}</div>
          </div>
          <div class="text-end">
            <div class="small text-muted">Available</div>
            <div class="fw-semibold">${g.available}</div>
          </div>
        </div>
      </button>
    `).join('');
  } catch (e) {
    showAlert('danger', 'Không tải được danh sách hàng: ' + e.message);
  }
}

window.addGood = function(goodId, sku, name, available){
  const ex = LINES.find(x => x.goodId === goodId);
  if (ex) { ex.qty = Math.min(ex.qty + 1, available); renderLines(); return; }
  LINES.push({ goodId, sku, name, available, qty: 1, batchId: null });
  renderLines();
};

// ====== Lines ======
function renderLines() {
  const tb = el('lineBody');
  if (!LINES.length) {
    tb.innerHTML = `<tr><td colspan="7" class="text-center text-muted py-4">No items</td></tr>`;
    return;
  }
  tb.innerHTML = LINES.map((x, idx) => `
    <tr>
      <td>${idx+1}</td>
      <td>${esc(x.sku||'')}</td>
      <td>${esc(x.name||'')}</td>
      <td class="text-center">${x.available}</td>
      <td class="text-end">
        <input type="number" min="1" step="1" value="${x.qty}"
               class="form-control form-control-sm text-end"
               oninput="onQty(${idx}, this.value)" />
      </td>
      <td class="text-end">
        <input type="number" min="0" step="1" value="${x.batchId??''}"
               class="form-control form-control-sm text-end"
               oninput="onBatch(${idx}, this.value)" />
      </td>
      <td class="text-end">
        <button class="btn btn-sm btn-outline-danger" onclick="removeLine(${idx})">
          <i class="bi bi-x-circle"></i>
        </button>
      </td>
    </tr>
  `).join('');
}

window.onQty = function(idx, v){
  v = Number(v||0);
  if (isNaN(v) || v < 1) v = 1;
  if (v > LINES[idx].available) showAlert('warning', `Số lượng vượt Available (${LINES[idx].available}).`);
  LINES[idx].qty = v;
};

window.onBatch = function(idx, v){
  LINES[idx].batchId = v ? Number(v) : null;
};

window.removeLine = function(idx){
  LINES.splice(idx,1);
  renderLines();
};

// ====== Actions ======
function validateInput(){
  if (!FROM_ID) { showAlert('warning','Chưa chọn From (Warehouse).'); return false; }
  if (!TO_ID) { showAlert('warning','Không xác định được To (Store).'); return false; }
  if (!LINES.length) { showAlert('warning','Chưa có item nào.'); return false; }
  for (const x of LINES){
    if (!x.goodId || !x.qty || x.qty < 1){
      showAlert('warning','Số lượng không hợp lệ.'); return false;
    }
  }
  return true;
}

// Lưu nháp: POST /api/transfers
async function saveDraft(){
  if (!validateInput()) return;

  setBusy(true, 'save');
  try {
    const body = {
      fromLocationId: FROM_ID,
      toLocationId: TO_ID,
      items: LINES.map(x => ({ goodId: x.goodId, quantity: x.qty, batchId: x.batchId }))
    };
    const res = await api(`/transfers`, 'POST', body, true);
    if (res && (res.transferID || res.TransferID)) showAlert('success', 'Đã lưu nháp (Draft).');
    else showAlert('warning', 'Phản hồi không rõ từ máy chủ.');
  } catch (e) {
    showAlert('danger', 'Lỗi khi lưu nháp: ' + e.message);
  } finally {
    setBusy(false, 'save');
  }
}

// Submit: tạo Draft → submit(id)
async function submitTransfer(){
  if (!validateInput()) return;
  if (!confirm('Submit transfer này? Sau khi submit sẽ không chỉnh sửa được.')) return;

  setBusy(true, 'submit');
  try {
    // 1) Create Draft
    const created = await api(`/transfers`, 'POST', {
      fromLocationId: FROM_ID,
      toLocationId: TO_ID,
      items: LINES.map(x => ({ goodId: x.goodId, quantity: x.qty, batchId: x.batchId }))
    }, true);

    const tid = created?.transferID || created?.TransferID || created?.id;
    if (!tid) throw new Error('Không tạo được draft.');

    // 2) Submit → Draft -> Approved
    await api(`/transfers/${tid}/submit`, 'POST', null, true);

    showAlert('success', 'Đã submit transfer.');
    setTimeout(() => location.href = '../transfer_list/transfer_list.html', 600);
  } catch (e) {
    showAlert('danger', 'Lỗi khi submit: ' + e.message);
  } finally {
    setBusy(false, 'submit');
  }
}
