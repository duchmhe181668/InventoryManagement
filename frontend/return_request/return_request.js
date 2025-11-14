/* ==========================================
   RETURN REQUEST (STORE -> WAREHOUSE)
   Logic: Trả hàng về Kho
   ========================================== */

const API_BASE = 'https://localhost:7225';

// ============ AUTHENTICATION ============
function parseJwt(token) {
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(atob(base64).split('').map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2)).join(''));
    return JSON.parse(jsonPayload);
  } catch { return null; }
}

let AUTH_TOKEN = '';
function checkAuth() {
  const token = localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken');
  if (!token) {
    window.location.href = '../login.html'; return false;
  }
  AUTH_TOKEN = token;
  return true;
}

// ============ UTILS ============
const el = id => document.getElementById(id);
const esc = s => (s ?? '').toString().replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const debounce = (fn, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); } };

function showToast(msg, type = 'primary') {
  const ct = el('toastContainer');
  const bg = type === 'error' ? 'text-bg-danger' : (type === 'success' ? 'text-bg-success' : 'text-bg-light');
  const html = `
    <div class="toast align-items-center ${bg} border-0" role="alert" aria-live="assertive" aria-atomic="true">
      <div class="d-flex">
        <div class="toast-body fw-semibold">${esc(msg)}</div>
        <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
      </div>
    </div>`;
  ct.insertAdjacentHTML('beforeend', html);
  const tEl = ct.lastElementChild;
  const t = new bootstrap.Toast(tEl, { delay: 3000 });
  t.show();
  tEl.addEventListener('hidden.bs.toast', () => tEl.remove());
}

// ============ STATE ============
let FROM_ID = null; // Store ID (Cửa hàng của mình)
let TO_ID = null;   // Warehouse ID (Kho đích)
let ITEMS = [];
let CURRENT_ID = null;
let IS_VIEW_ONLY = false;

const goodBox = el('goodResults');
const goodUl = goodBox.querySelector('ul');

// ============ INITIALIZATION ============
window.addEventListener('DOMContentLoaded', async () => {
  if (!checkAuth()) return;

  // Events
  el('btnClearAll').addEventListener('click', () => { if (ITEMS.length && confirm('Xóa hết danh sách trả?')) { ITEMS = []; renderItems(); } });
  el('btnDraft').addEventListener('click', () => saveOrSubmit(true));
  el('btnSubmit').addEventListener('click', () => saveOrSubmit(false));
  
  const inpSearch = el('goodSearch');
  inpSearch.addEventListener('input', debounce((e) => searchGoods(e.target.value), 400));
  document.addEventListener('click', (e) => {
      if (!goodBox.contains(e.target) && e.target !== inpSearch) goodBox.classList.add('d-none');
  });

  const params = new URLSearchParams(location.search);
  const id = params.get('id');
  const edit = params.get('edit');

  if (id) {
      CURRENT_ID = parseInt(id);
      IS_VIEW_ONLY = (edit !== 'true');
      await loadTransferDetail(CURRENT_ID);
  } else {
      await loadHeaderNew();
  }
  
  renderItems();
});

// ============ API CALLS ============
async function api(endpoint, method = 'GET', body = null) {
  const opts = {
      method,
      headers: { 
          'Authorization': `Bearer ${AUTH_TOKEN}`,
          ...(body ? { 'Content-Type': 'application/json' } : {})
      }
  };
  if (body) opts.body = JSON.stringify(body);

  try {
      const res = await fetch(`${API_BASE}${endpoint}`, opts);
      if (!res.ok) {
          const txt = await res.text();
          try {
              const json = JSON.parse(txt);
              throw new Error(json.detail || json.title || json.message || txt);
          } catch { throw new Error(txt || `HTTP ${res.status}`); }
      }
      const text = await res.text();
      return text ? JSON.parse(text) : null;
  } catch (e) { throw e; }
}

// ============ LOGIC TRẢ HÀNG ============

// 1. Load Header Mới (Ngược lại so với Transfer thường)
async function loadHeaderNew() {
  try {
      // Lấy thông tin User (Store) -> Làm Nguồn (FROM)
      const me = await api('/api/auth/profile');
      if (me?.storeDefaultLocationId) {
          FROM_ID = me.storeDefaultLocationId;
          el('fromLocationName').value = me.storeDefaultLocationName;
          el('fromLocation').value = FROM_ID;
      } else {
          showToast('Tài khoản này chưa được gán Cửa hàng.', 'error');
          return;
      }
      el('createdByName').value = me?.name || 'Tôi';

      // Lấy Kho (Warehouse) -> Làm Đích (TO)
      const whs = await api('/api/lookups/locations?type=WAREHOUSE&active=true');
      const defaultWh = whs?.[0];
      if (defaultWh) {
          TO_ID = defaultWh.locationID;
          el('toLocationName').value = defaultWh.name;
          el('toLocation').value = TO_ID;
      } else {
          showToast('Không tìm thấy Kho để trả hàng.', 'error');
      }

  } catch (e) {
      showToast('Lỗi tải dữ liệu: ' + e.message, 'error');
  }
}

// 2. Load Chi tiết (Sửa/Xem)
async function loadTransferDetail(id) {
  try {
      const t = await api(`/api/transfers/${id}`);
      if (!t) throw new Error('Không tìm thấy phiếu.');

      FROM_ID = t.fromLocationID;
      TO_ID = t.toLocationID;

      el('fromLocationName').value = t.fromLocationName;
      el('toLocationName').value = t.toLocationName;
      el('createdByName').value = t.createdByName || `User #${t.createdBy}`;
      
      if (IS_VIEW_ONLY) {
          el('pageTitle').textContent = `Xem Phiếu Trả #${id}`;
          el('viewModeBadge').classList.remove('d-none');
          el('goodSearch').disabled = true;
          el('goodSearch').placeholder = '---';
          el('btnClearAll').classList.add('d-none');
          el('btnDraft').classList.add('d-none');
          el('btnSubmit').classList.add('d-none');
      } else {
          el('pageTitle').textContent = `Sửa Phiếu Trả #${id}`;
      }

      ITEMS = (t.items || []).map(i => ({
          goodId: i.goodID,
          sku: i.sku,
          name: i.name,
          barcode: i.barcode,
          available: i.available, // Tồn tại Store
          qty: i.quantity
      }));

  } catch (e) {
      showToast(e.message, 'error');
      el('goodSearch').disabled = true;
      el('btnSubmit').disabled = true;
  }
}

// 3. Tìm hàng (Tìm tồn kho tại Store)
async function searchGoods(kw) {
  if (!kw || !FROM_ID) { goodBox.classList.add('d-none'); return; }
  try {
      // Gọi API tìm tồn kho tại FROM_ID (chính là Store)
      const data = await api(`/api/lookups/stock-available?locationId=${FROM_ID}&kw=${encodeURIComponent(kw)}`);
      
      goodUl.innerHTML = '';
      if (!data || data.length === 0) {
          goodUl.innerHTML = `<li class="list-group-item text-muted p-3">Không tìm thấy hàng trong kho cửa hàng.</li>`;
      } else {
          data.forEach(g => {
              const li = document.createElement('li');
              li.className = 'list-group-item list-group-item-action py-2';
              li.innerHTML = `
                  <div class="d-flex justify-content-between align-items-center">
                      <div>
                          <div class="fw-bold text-dark">${esc(g.goodName)}</div>
                          <div class="small text-muted">SKU: ${esc(g.sku)}</div>
                      </div>
                      <div class="text-end">
                          <div class="small text-muted">Có thể trả</div>
                          <span class="badge bg-danger-subtle text-danger border border-danger">${g.available}</span>
                      </div>
                  </div>`;
              
              li.addEventListener('click', () => addItem(g));
              goodUl.appendChild(li);
          });
      }
      goodBox.classList.remove('d-none');
  } catch (e) { console.error(e); }
}

// 4. Add Item
function addItem(g) {
  const idx = ITEMS.findIndex(i => i.goodId === g.goodID);
  if (idx >= 0) { ITEMS[idx].qty++; } 
  else {
      ITEMS.push({
          goodId: g.goodID,
          sku: g.sku,
          name: g.goodName,
          barcode: g.barcode,
          available: g.available,
          qty: 1
      });
  }
  renderItems();
  goodBox.classList.add('d-none');
  el('goodSearch').value = '';
  el('goodSearch').focus();
}

// 5. Render
function renderItems() {
  const tbody = el('itemsBody');
  if (ITEMS.length === 0) {
      tbody.innerHTML = `<tr><td colspan="7" class="text-center text-muted py-5">Chưa có mặt hàng nào để trả.</td></tr>`;
      el('totalItems').textContent = '0';
      return;
  }

  tbody.innerHTML = ITEMS.map((item, i) => {
      const isOver = !IS_VIEW_ONLY && (item.qty > item.available);
      
      const qtyInput = IS_VIEW_ONLY 
          ? `<span class="fw-bold fs-6">${item.qty}</span>`
          : `<input type="number" class="form-control form-control-sm qty-input mx-auto ${isOver ? 'is-invalid border-danger' : ''}" 
                    value="${item.qty}" min="1" onchange="updateQty(${i}, this.value)">`;

      const deleteBtn = IS_VIEW_ONLY ? '-' : 
          `<button class="btn btn-sm btn-outline-danger border-0" onclick="removeItem(${i})"><i class="fa-solid fa-trash-can"></i></button>`;

      return `
      <tr>
          <td class="text-muted">${i + 1}</td>
          <td>${esc(item.sku)}</td>
          <td>${esc(item.barcode)}</td>
          <td class="fw-semibold text-primary">${esc(item.name)}</td>
          <td class="text-center ${isOver ? 'text-danger fw-bold' : 'text-dark'}">${item.available}</td>
          <td class="text-center">${qtyInput}</td>
          <td class="text-center">${deleteBtn}</td>
      </tr>`;
  }).join('');

  el('totalItems').textContent = ITEMS.length;
}

window.updateQty = (index, val) => {
  const v = parseInt(val);
  if (v > 0) { ITEMS[index].qty = v; renderItems(); }
};
window.removeItem = (index) => {
  ITEMS.splice(index, 1); renderItems();
};

// 6. Save/Submit
async function saveOrSubmit(isDraft) {
  if (IS_VIEW_ONLY) return;
  if (!FROM_ID || !TO_ID) return showToast('Thiếu thông tin Cửa hàng/Kho.', 'error');
  if (!ITEMS.length) return showToast('Danh sách trả hàng trống.', 'error');

  const invalidItem = ITEMS.find(i => i.qty > i.available);
  if (invalidItem) {
      showToast(`Lỗi: Số lượng trả của "${invalidItem.name}" vượt quá tồn kho hiện có (${invalidItem.available}).`, 'error');
      return;
  }

  const method = CURRENT_ID ? 'PUT' : 'POST';
  const payload = {
      transferID: CURRENT_ID,
      fromLocationID: FROM_ID,
      toLocationID: TO_ID,
      items: ITEMS.map(i => ({ goodID: i.goodId, quantity: i.qty, batchID: null }))
  };

  try {
      const res = await api('/api/transfers', method, payload);
      const newId = res?.transferID || CURRENT_ID;

      if (!isDraft) {
          await api(`/api/transfers/${newId}/submit`, 'POST');
          showToast('Đã gửi phiếu trả hàng thành công!', 'success');
      } else {
          showToast('Đã lưu nháp.', 'success');
      }

      setTimeout(() => window.location.href = '../transfer_list/transfer_list.html', 1500);

  } catch (e) {
      showToast('Lỗi: ' + e.message, 'error');
  }
}