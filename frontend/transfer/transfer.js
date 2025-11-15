

const API_BASE = 'https://localhost:7225';

// AUTH (Dán trực tiếp)
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

let CURRENT_TRANSFER_ID = null;
let IS_VIEW_ONLY = false; 


// ===== Boot =====
window.addEventListener('DOMContentLoaded', async () => {
  if (!checkAuth()) return;

  // UI refs
  goodBox = el('goodResults'); goodUl = goodBox.querySelector('ul');

  // events
  el('btnClearAll').addEventListener('click', ()=>{ if (ITEMS.length && confirm('Bạn chắc chắn muốn xoá hết mặt hàng?')) { ITEMS=[]; renderItems(); } });
  el('btnDraft').addEventListener('click', ()=> saveOrSubmit(true));
  el('btnSubmit').addEventListener('click', ()=> saveOrSubmit(false));
  el('goodSearch').addEventListener('input', debounce(onSearchGoods, 300));

  // click outside dropdown
  document.addEventListener('click', (e)=>{
    const input = el('goodSearch');
    if (goodBox && input && !goodBox.contains(e.target) && e.target !== input)
      goodBox.classList.add('d-none');
  });
  
  const urlParams = new URLSearchParams(window.location.search);
  const id = urlParams.get('id');
  const isEdit = urlParams.get('edit') === 'true';

  if (id) {
    CURRENT_TRANSFER_ID = parseInt(id, 10);
    IS_VIEW_ONLY = !isEdit; 
    
    await loadForEdit(CURRENT_TRANSFER_ID, IS_VIEW_ONLY);
    
  } else {
    try { 
      await loadHeaderNew(); 
    } catch (e) { 
      showToast('Không tải được dữ liệu ban đầu. ' + e.message, 'error'); 
    }
  }

  renderItems(); 
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
    if (res.status === 401) { alert('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.'); location.href='../login.html'; }
    if (res.status === 403) { alert('Bạn không có quyền thực hiện.'); location.href='../home.html'; }
    
    let txt = `Lỗi HTTP ${res.status}`;
    try {
        const errData = await res.json();
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
  // === SỬA ĐỔI: Gọi /api/lookups/locations ===
  const whs = await api('/api/lookups/locations?type=WAREHOUSE&active=true', 'GET', null, true);
  const defaultWh = whs?.[0];
  if (defaultWh) {
      el('fromLocationName').value = esc(defaultWh.name);
      el('fromLocation').value = defaultWh.locationID;
      FROM_ID = defaultWh.locationID;
  }

  // API /api/auth/profile vẫn giữ nguyên
  const me = await api('/api/auth/profile', 'GET', null, true);
  TO_ID = me?.storeDefaultLocationId || null;
  el('toLocation').value = TO_ID || '';
  el('toLocationName').value = me?.storeDefaultLocationName || (TO_ID ? `#${TO_ID}` : '');
  el('createdByName').value = me?.name || me?.username || '';
}

// Tải dữ liệu cho chế độ Sửa/Xem 
async function loadForEdit(id, isViewOnly) {
  try {
    // Gọi API GetTransfer (đã sửa ở Bước 1)
    const t = await api(`/api/transfers/${id}`, 'GET', null, true);
    
    FROM_ID = t.fromLocationID;
    TO_ID = t.toLocationID;
    
    // === SỬA ĐỔI: Đọc 'available' từ API (fix lỗi 'undefined') ===
    ITEMS = t.items.map(item => ({
        goodId: item.goodID,
        sku: item.sku,
        name: item.name,
        barcode: item.barcode, 
        available: item.available, 
        qty: item.quantity,
        batchId: null
    }));
    
    el('fromLocationName').value = esc(t.fromLocationName);
    el('fromLocation').value = t.fromLocationID;
    el('toLocationName').value = esc(t.toLocationName);
    el('toLocation').value = t.toLocationID;
    el('createdByName').value = esc(t.createdByName || `User ID: ${t.createdBy}`); 

    if (isViewOnly) {
      el('pageTitle').textContent = `Xem Phiếu Xin Hàng #${id}`;
      el('fromLocationName').disabled = true; 
      el('goodSearch').disabled = true;
      el('goodSearch').placeholder = 'Chế độ chỉ xem';
      el('btnClearAll').style.display = 'none';
      el('btnDraft').style.display = 'none';
      el('btnSubmit').style.display = 'none';

      el('th-available').style.display = 'none';
      el('th-delete').style.display = 'none';

    } else {
      el('pageTitle').textContent = `Sửa Phiếu Xin Hàng #${id}`;
      el('fromLocationName').disabled = true; 
    }
    
  } catch(e) {
      showToast('Lỗi khi tải phiếu: ' + e.message, 'error');
      el('fromLocationName').disabled = true;
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
    // === SỬA ĐỔI: Gọi /api/lookups/stock-available ===
    const data = await api(`/api/lookups/stock-available?locationId=${FROM_ID}&kw=${encodeURIComponent(q)}`, 'GET', null, true);
    goodUl.innerHTML = '';
    
    if (!data || data.length === 0) {
        goodUl.innerHTML = '<li class="list-group-item text-muted">Không tìm thấy sản phẩm.</li>';
        goodBox.classList.remove('d-none');
        return;
    }
    
    data.forEach(g => {
      const li = document.createElement('li');
      li.className = 'list-group-item list-group-item-action';
      
      li.innerHTML = `
        <div class="d-flex justify-content-between">
          <div>
            <div class="fw-semibold">${esc(g.goodName)}</div>
            <div class="small text-muted">SKU: ${esc(g.sku||'')} | Barcode: ${esc(g.barcode||'')}</div>
          </div>
          <div class="text-end">
            <div class="small text-muted">Tồn khả dụng</div>
            <div class="fw-semibold">${g.available}</div>
          </div>
        </div>`;
        
      li.addEventListener('click', ()=>{
        const idx = ITEMS.findIndex(x => x.goodId === g.goodID); 
        
        if (idx >= 0) {
          ITEMS[idx].qty += 1; 
        } else {
          ITEMS.push({
            goodId: g.goodID, 
            sku: g.sku, 
            name: g.goodName,
            barcode: g.barcode, 
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
    console.error(err); showToast('Không tải được danh sách hàng: ' + err.message, 'error');
  }
}

// ===== Render lines =====
function renderItems(){
  const tb = el('itemsBody');
  
  // === SỬA ĐỔI: Cập nhật colspan động (7 khi Sửa, 5 khi Xem) ===
  const colspan = IS_VIEW_ONLY ? 5 : 7;
  
  if (!ITEMS.length) { 
    tb.innerHTML = `<tr><td colspan="${colspan}" class="text-center text-muted">Chưa có mặt hàng nào</td></tr>`; 
    el('totalItems').textContent = '0'; 
    return;
  }

  tb.innerHTML = ITEMS.map((x, i)=> {
    
    const isOver = x.qty > x.available;
    const invalidClass = isOver ? 'is-invalid' : ''; 

    const qtyCell = IS_VIEW_ONLY ?
      `<td class="text-center ${isOver ? 'text-danger fw-bold' : ''}">${x.qty}</td>` :
      `<td>
         <input type="number" min="1" step="1" class="form-control form-control-sm qty-input ${invalidClass}"
                value="${x.qty}" data-idx="${i}">
       </td>`;

    // === SỬA ĐỔI: Ẩn <td> 'Available' và 'Delete' khi Chỉ Xem ===
    const availableCell = IS_VIEW_ONLY ? 
        '' : // Ẩn khi Chỉ Xem
        `<td class="text-center ${isOver ? 'text-danger fw-bold' : ''}">${x.available}</td>`;
        
    const deleteCell = IS_VIEW_ONLY ?
        '' : // Ẩn khi Chỉ Xem
        `<td class="text-center">
           <button class="btn btn-sm btn-outline-danger icon-btn" data-del="${i}">
             <i class="fa-solid fa-trash-can fa-sm"></i>
           </button>
         </td>`;

    return `
      <tr>
        <td>${i+1}</td>
        <td>${esc(x.sku||'')}</td>
        <td>${esc(x.barcode||'')}</td>
        <td>${esc(x.name||'')}</td>
        
        ${availableCell} ${qtyCell}
        ${deleteCell} </tr>
    `;
  }).join('');

  el('totalItems').textContent = String(ITEMS.length);

  if (!IS_VIEW_ONLY) {
    
    tb.querySelectorAll('input[data-idx]').forEach(inp=>{
      inp.addEventListener('input', e=>{ 
        const i = Number(e.target.dataset.idx);
        if (!ITEMS[i]) return;

        let v = Number(e.target.value);
        if (!Number.isFinite(v) || v < 1) {}
        
        ITEMS[i].qty = v; 
        
        const isOver = v > ITEMS[i].available;
        e.target.classList.toggle('is-invalid', isOver);
        
        const availableCell = e.target.closest('tr').querySelector('td:nth-child(5)');
        if (availableCell) {
            availableCell.classList.toggle('text-danger', isOver);
            availableCell.classList.toggle('fw-bold', isOver);
        }
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
  if (IS_VIEW_ONLY) return false; 
  if (!FROM_ID) { showToast('Vui lòng chọn Kho (From).','warning'); return false; }
  if (!TO_ID)   { showToast('Không xác định được Cửa hàng (To).','warning'); return false; }
  if (!ITEMS.length) { showToast('Chưa có mặt hàng nào.','warning'); return false; }

  let hasError = false;
  
  for (const item of ITEMS) {
    if (!item.goodId || !item.qty || item.qty < 1) {
      showToast(`Số lượng của sản phẩm trong phiếu vượt mức tồn kho! Vui lòng kiểm tra lại!`,'warning'); 
      hasError = true;
      break;
    }
    
    // === SỬA ĐỔI: Luôn check tồn kho (kể cả khi Sửa) ===
    if (item.qty > item.available) {
        showToast(`Số lượng của sản phẩm trong phiếu vượt mức tồn kho! Vui lòng kiểm tra lại!`, 'danger');
        hasError = true;
        break; 
    }
  }
  
  return !hasError; 
}
async function saveOrSubmit(isDraft){
  if (!validateBeforeSave()) return;

  const isEditMode = !!CURRENT_TRANSFER_ID;
  const method = isEditMode ? 'PUT' : 'POST';
  const apiPath = '/api/transfers'; 

  try {
    const body = {
      fromLocationId: FROM_ID,
      toLocationId: TO_ID,
      items: ITEMS.map(x => ({ goodId: x.goodId, quantity: x.qty, batchId: null }))
    };

    if (isEditMode) {
      body.transferID = CURRENT_TRANSFER_ID;
    }

    const created = await api(apiPath, method, body, true);
    
    const tid = created?.transferID || created?.TransferID || CURRENT_TRANSFER_ID;
    if (!tid) throw new Error('Không xác định được ID phiếu.');

    if (!isDraft) {
      await api(`/api/transfers/${tid}/submit`, 'POST', null, true);
      showToast('Đã gửi yêu cầu thành công.', 'success');
    } else {
      showToast('Đã lưu nháp thành công.', 'success');
    }

    setTimeout(()=> location.href = '../transfer_list/transfer_list.html', 800);
  } catch (e) {
    console.error(e);
    showToast(`Lỗi khi lưu: ${e.message}`, 'error');
  }
}