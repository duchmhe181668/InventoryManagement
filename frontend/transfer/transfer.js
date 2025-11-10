/* =========================
   Transfer (TẠO MỚI / SỬA / XEM)
   (Đã sửa để hỗ trợ ?id=... từ URL)
   ========================= */

// === SỬA ĐỔI: Sửa API_BASE để fix lỗi 404 (/api/api/...) ===
const API_BASE = 'https://localhost:7225';

// ===== AUTH (Dán trực tiếp) =====
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

  let userRole = localStorage.getItem('authRole') || sessionStorage.getItem('authRole');
  if (!userRole) {
    const payload = parseJwt(token);
    userRole = payload ? (payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '') : '';
  }
  const role = (userRole || '').toLowerCase();
  const allowed = ['storemanager', 'administrator'];
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
let ITEMS = [];     // {goodId, sku, name, available, qty, batchId: null}

let goodBox, goodUl;

// === SỬA ĐỔI: Thêm State cho chế độ Sửa/Xem ===
let CURRENT_TRANSFER_ID = null;
let IS_VIEW_ONLY = false; // Chế độ "Xem" (khóa tất cả)


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

  // click outside dropdown
  document.addEventListener('click', (e)=>{
    const input = el('goodSearch');
    if (goodBox && input && !goodBox.contains(e.target) && e.target !== input)
      goodBox.classList.add('d-none');
  });
  
  // === SỬA ĐỔI: Kiểm tra URL (Đây là phần quan trọng nhất) ===
  const urlParams = new URLSearchParams(window.location.search);
  const id = urlParams.get('id');
  const isEdit = urlParams.get('edit') === 'true';

  if (id) {
    // Đây là chế độ Sửa hoặc Xem
    CURRENT_TRANSFER_ID = parseInt(id, 10);
    IS_VIEW_ONLY = !isEdit; // Nếu không có ?edit=true -> Chuyển sang chế độ CHỈ XEM
    
    await loadForEdit(CURRENT_TRANSFER_ID, IS_VIEW_ONLY);
    
  } else {
    // Đây là chế độ Tạo mới
    try { 
      await loadHeaderNew(); // Tải header cho phiếu mới
    } catch (e) { 
      showToast('Không tải được dữ liệu ban đầu', 'error'); 
    }
  }

  renderItems(); // Vẽ bảng (có thể trống nếu là phiếu mới)
});

// ===== API helper =====
async function api(path, method='GET', body=null, mustOk=false) {
  const res = await fetch(`${API_BASE}${path}`, { // Sửa lỗi 404: API_BASE không chứa /api
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
    
    let txt = `HTTP ${res.status}`;
    try {
        const errData = await res.json();
        // Lấy lỗi chi tiết từ ASP.NET Core (nếu có)
        txt = errData.detail || errData.title || errData.message || JSON.stringify(errData);
    } catch(e) {
        txt = await res.text() || txt;
    }
    
    if (mustOk) throw new Error(txt);
  }
  try { return await res.json(); } catch { return null; }
}

// ===== Header (Cho phiếu MỚI) =====
async function loadHeaderNew() {
  // warehouses
  const whs = await api('/api/locations?type=WAREHOUSE&active=true', 'GET', null, true);
  const sel = el('fromLocation');
  sel.innerHTML = (whs||[]).map(x => `<option value="${x.locationID}">${esc(x.name || ('#'+x.locationID))}</option>`).join('');
  FROM_ID = whs?.[0]?.locationID || null;
  sel.addEventListener('change', ()=>{ FROM_ID = Number(sel.value); ITEMS=[]; renderItems(); });

  // profile → store & created by
  const me = await api('/api/auth/profile', 'GET', null, true);
  TO_ID = me?.storeDefaultLocationId || null;
  el('toLocation').value = TO_ID || '';
  el('toLocationName').value = me?.storeDefaultLocationName || (TO_ID ? `#${TO_ID}` : '');
  el('createdByName').value = me?.name || me?.username || '';
}

// === SỬA ĐỔI: Hàm mới (Tải dữ liệu cho chế độ Sửa/Xem) ===
async function loadForEdit(id, isViewOnly) {
  try {
    // Gọi API GetTransfer (đã sửa ở Backend, trả về cả Barcode)
    const t = await api(`/api/transfers/${id}`, 'GET', null, true);
    
    // 1. Đổ dữ liệu vào State
    FROM_ID = t.fromLocationID;
    TO_ID = t.toLocationID;
    
    // === SỬA ĐỔI: API GetTransfer giờ đã trả về tên/sku/barcode ===
    ITEMS = t.items.map(item => ({
        goodId: item.goodID,
        sku: item.sku,
        name: item.name,
        barcode: item.barcode, // Thêm Barcode
        available: 0, // API này không trả về 'available', chấp nhận
        qty: item.quantity,
        batchId: null
    }));
    
    // 2. Đổ dữ liệu vào Form
    el('fromLocation').innerHTML = `<option value="${t.fromLocationID}">${esc(t.fromLocationName)}</option>`;
    el('toLocationName').value = esc(t.toLocationName);
    el('toLocation').value = t.toLocationID;
    
    el('createdByName').value = `User ID: ${t.createdBy}`; 

    // 3. Khóa giao diện nếu là "Xem" (View Only)
    if (isViewOnly) {
      el('pageTitle').textContent = `Xem Phiếu Xin Hàng #${id}`;
      el('fromLocation').disabled = true;
      el('goodSearch').disabled = true;
      el('goodSearch').placeholder = 'Chế độ chỉ xem';
      el('btnClearAll').style.display = 'none';
      el('btnDraft').style.display = 'none';
      el('btnSubmit').style.display = 'none';
    } else {
      el('pageTitle').textContent = `Sửa Phiếu Xin Hàng #${id}`;
      el('fromLocation').disabled = true; // Không cho đổi kho khi Sửa
    }
    
  } catch(e) {
      showToast('Lỗi khi tải phiếu: ' + e.message, 'error');
      // Khóa hết nếu tải lỗi
      el('fromLocation').disabled = true;
      el('goodSearch').disabled = true;
      el('btnDraft').style.display = 'none';
      el('btnSubmit').style.display = 'none';
  }
}

// ===== Search goods with Available at FROM =====
async function onSearchGoods(e){
  const q = e.target.value.trim();
  if (!q || !FROM_ID) { goodBox.classList.add('d-none'); goodUl.innerHTML=''; return; }
  try {
    // API (đã sửa) giờ trả về cả Barcode
    const data = await api(`/api/stocks/available?locationId=${FROM_ID}&kw=${encodeURIComponent(q)}`, 'GET', null, true);
    goodUl.innerHTML = '';
    (data || []).forEach(g => {
      const li = document.createElement('li');
      li.className = 'list-group-item list-group-item-action';
      
      // === SỬA ĐỔI: Hiển thị Barcode ===
      li.innerHTML = `
        <div class="d-flex justify-content-between">
          <div>
            <div class="fw-semibold">${esc(g.goodName)}</div>
            <div class="small text-muted">SKU: ${esc(g.sku||'')} | Barcode: ${esc(g.barcode||'')}</div>
          </div>
          <div class="text-end">
            <div class="small text-muted">Available</div>
            <div class="fw-semibold">${g.available}</div>
          </div>
        </div>`;
        
      li.addEventListener('click', ()=>{
        const idx = ITEMS.findIndex(x => x.goodId === g.goodID); 
        if (idx >= 0) {
          const newQty = ITEMS[idx].qty + 1;
          ITEMS[idx].qty = Math.min(newQty, Number(g.available)||999999); 
        } else {
          // === SỬA ĐỔI: Lưu cả Barcode ===
          ITEMS.push({
            goodId: g.goodID, 
            sku: g.sku, 
            name: g.goodName,
            barcode: g.barcode, // Thêm Barcode
            available: Number(g.available)||0, 
            qty: 1, 
            batchId: null
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
// ===== Render lines =====
function renderItems(){
  const tb = el('itemsBody');
  
  // === SỬA ĐỔI: Colspan = 7 (vì thêm Barcode) ===
  if (!ITEMS.length) { 
    tb.innerHTML = `<tr><td colspan="7" class="text-center text-muted">No items</td></tr>`; 
    return;
  }

  const readOnly = IS_VIEW_ONLY ? 'readonly disabled' : '';

  tb.innerHTML = ITEMS.map((x, i)=>`
      <tr>
        <td>${i+1}</td>
        <td>${esc(x.sku||'')}</td>
        <td>${esc(x.barcode||'')}</td> <td>${esc(x.name||'')}</td>
        
        ${CURRENT_TRANSFER_ID ? 
          '<td class="text-center text-muted">--</td>' : 
          `<td class="text-center">${x.available}</td>`
        }
        
        <td>
          <input type="number" min="1" step="1" class="form-control form-control-sm qty-input"
                 value="${x.qty}" data-idx="${i}" ${readOnly}>
        </td>
        <td class="text-center">
          ${IS_VIEW_ONLY ? '---' : 
          `<button class="btn btn-sm btn-outline-danger icon-btn" data-del="${i}">
            <i class="fa-solid fa-trash-can fa-sm"></i>
           </button>`}
        </td>
      </tr>
    `).join('');

  el('totalItems').textContent = String(ITEMS.length);

  // bind inputs (Chỉ bind nếu không phải View Only)
  if (!IS_VIEW_ONLY) {
    tb.querySelectorAll('input[data-idx]').forEach(inp=>{
      inp.addEventListener('change', e=>{
        const i = Number(e.target.dataset.idx);
        let v = Number(e.target.value);
        if (!Number.isFinite(v) || v < 1) v = 1;
        
        if (!CURRENT_TRANSFER_ID && v > ITEMS[i].available) {
           showToast(`Vượt Available (${ITEMS[i].available}).`, 'warning');
           v = ITEMS[i].available;
        }
        
        ITEMS[i].qty = v; e.target.value = v;
      });
    });

    tb.querySelectorAll('button[data-del]').forEach(btn=>{
      btn.addEventListener('click', e=>{
        const i = Number(e.currentTarget.dataset.del);
        ITEMS.splice(i,1); renderItems();
      });
    });
  }
}

// ===== Save Draft / Submit =====
function validateBeforeSave(){
  if (IS_VIEW_ONLY) return false; // Không cho lưu ở chế độ xem
  if (!FROM_ID) { showToast('Chọn From (Warehouse).','warning'); return false; }
  if (!TO_ID)   { showToast('Không xác định được Store.','warning'); return false; }
  if (!ITEMS.length) { showToast('Chưa có mặt hàng.','warning'); return false; }
  if (ITEMS.some(x => !x.goodId || !x.qty || x.qty < 1)) { showToast('Số lượng không hợp lệ.','warning'); return false; }
  return true;
}

async function saveOrSubmit(isDraft){
  if (!validateBeforeSave()) return;

  // === SỬA ĐỔI: Quyết định API (POST hay PUT) ===
  const isEditMode = !!CURRENT_TRANSFER_ID;
  const method = isEditMode ? 'PUT' : 'POST';
  const apiPath = '/api/transfers';

  try {
    const body = {
      fromLocationId: FROM_ID,
      toLocationId: TO_ID,
      items: ITEMS.map(x => ({ goodId: x.goodId, quantity: x.qty, batchId: null }))
    };

    // Nếu là Sửa (PUT), cần thêm TransferID vào body
    if (isEditMode) {
      body.transferID = CURRENT_TRANSFER_ID;
    }

    const created = await api(apiPath, method, body, true);
    
    // Lấy ID (hoặc từ POST, hoặc dùng ID cũ)
    const tid = created?.transferID || created?.TransferID || CURRENT_TRANSFER_ID;
    if (!tid) throw new Error('Không xác định được ID phiếu.');

    // 2) Nếu submit: gọi /submit
    if (!isDraft) {
      // Chỉ submit nếu đang là Draft (hoặc mới tạo)
      await api(`/api/transfers/${tid}/submit`, 'POST', null, true);
      showToast('Đã gửi yêu cầu transfer.', 'success');
    } else {
      showToast('Đã lưu nháp transfer.', 'success');
    }

    // Quay về trang danh sách
    setTimeout(()=> location.href = '../transfer_list/transfer_list.html', 800);
  } catch (e) {
    console.error(e);
    showToast(`Lỗi: ${e.message}`, 'error');
  }
}