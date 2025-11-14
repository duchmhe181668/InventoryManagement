/* =========================
   AUTH HELPER (Dán trực tiếp)
   ========================= */

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

function checkAuth(allowedRoles = []) {
  const token = localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken');
  if (!token) {
    alert("Bạn chưa đăng nhập hoặc phiên đã hết hạn. Vui lòng đăng nhập lại.");
    window.location.href = '../login.html'; 
    return { token: null, hasPermission: false, role: null };
  }
  
  let userRole = localStorage.getItem('authRole') || sessionStorage.getItem('authRole');
  
  if (!userRole) {
    const payload = parseJwt(token);
    userRole = payload ? (payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '') : '';
  }
  
  const roleToCompare = (userRole || '').toLowerCase();

  if (allowedRoles.length > 0) {
      if (!allowedRoles.includes(roleToCompare)) {
          alert("Bạn không có quyền truy cập chức năng này.");
          window.location.href = '../home.html'; 
          return { token, hasPermission: false, role: roleToCompare };
      }
  }
  
  return { token, hasPermission: true, role: roleToCompare };
}

/* =========================
   BẮT ĐẦU CODE CỦA TRANG WM LIST
   (Logic FEFO Tự động)
   ========================= */

const API_BASE = 'https://localhost:7225';
let AUTH_TOKEN = null;
let MODAL_INSTANCE = null;
let CURRENT_MODAL_ID = null;
// Mảng này sẽ lưu PickList (đã chia lô) từ backend
let CURRENT_MODAL_ITEMS = []; 

// ====== Utils ======
const el = id => document.getElementById(id);
const esc = s => (s ?? '').toString().replace(/[&<>"'\\]/g, m => ({
  '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;','\\':'&#92;'
}[m]));

function getStatusBadge(status) {
    const s = (status || 'draft').toLowerCase();
    const map = {
        'draft': 'status-draft',
        'approved': 'status-success', 
        'shipping': 'status-shipping',
        'shipped': 'status-shipping',
        'receiving': 'status-warning',
        'received': 'status-success',
        'cancelled': 'status-secondary'
    };
    const defaultClass = 'status-info'; 
    const cssClass = map[s] || defaultClass;
    return `<span class="badge ${cssClass}">${esc(status)}</span>`;
}


// ====== Boot ======
window.addEventListener('DOMContentLoaded', init);

function init() {
  // 1) Auth (Chỉ WM/Admin)
  const auth = checkAuth(['warehousemanager', 'administrator']); 
  if (!auth.hasPermission) return;
  AUTH_TOKEN = auth.token;

  // 2) Khởi tạo Modal
  MODAL_INSTANCE = new bootstrap.Modal(el('processModal'));

  // 3) Gắn sự kiện
  el('status').addEventListener('change', loadTransfers);

  // Gắn sự kiện cho các nút hành động
  el('btnAccept').addEventListener('click', handleAccept);
  el('btnReject').addEventListener('click', handleReject); // <--- NÚT MỚI

  // 4) Tải danh sách
  loadTransfers();
}

// ====== API helper ======
async function api(path, method = 'GET', body = null) {
  const resp = await fetch(`${API_BASE}${path}`, { 
    method,
    headers: {
      'Authorization': `Bearer ${AUTH_TOKEN}`,
      ...(body ? { 'Content-Type': 'application/json' } : {})
    },
    body: body ? JSON.stringify(body) : null
  });

  if (!resp.ok) {
    if (resp.status === 401 || resp.status === 403) checkAuth(['warehousemanager', 'administrator']);
    
    const rawText = await resp.text();
    let errMessage = `Lỗi HTTP ${resp.status}`;

    try {
        const errData = JSON.parse(rawText);
        errMessage = errData.detail || errData.title || errData.message || JSON.stringify(errData);
    } catch {
        if (rawText) errMessage = rawText;
    }
    
    throw new Error(errMessage);
  }

  try {
    const text = await resp.text();
    return text ? JSON.parse(text) : null;
  } catch (e) {
    return null;
  }
}

// ====== Tải và Vẽ Bảng (List) ======
async function loadTransfers() {
  const status = el('status').value;
  const tbody = el('tbody');
  tbody.innerHTML = `<tr><td colspan="5" class="text-center p-4"><div class="spinner-border spinner-border-sm"></div> Đang tải...</td></tr>`;

  try {
    // API /api/warehouse/transfers (WarehouseController)
    const transfers = await api(`/api/warehouse/transfers?status=${encodeURIComponent(status)}`);
    renderTable(transfers);
  } catch (e) {
    tbody.innerHTML = `<tr><td colspan="5" class="text-center text-danger p-4">Lỗi tải dữ liệu: ${e.message}</td></tr>`;
  }
}

function renderTable(transfers) {
  const tbody = el('tbody');
  if (!transfers || transfers.length === 0) {
    tbody.innerHTML = `<tr><td colspan="5" class="text-center text-muted p-4">Không tìm thấy phiếu nào.</td></tr>`;
    return;
  }

  tbody.innerHTML = transfers.map(t => {
    const isApproved = (t.status || '').toLowerCase() === 'approved';
    
    let actionButton = '';

    if (isApproved) {
      // Nút để mở Modal Duyệt (FEFO)
      actionButton = `<button class="btn btn-sm btn-success" data-id="${t.transferID}" data-action="process">
                        <i class="fa-solid fa-check me-1"></i> Duyệt & Xuất
                      </button>`;
    } else {
      // Nút xem (đã hoàn thành hoặc nháp)
      actionButton = `<button class="btn btn-sm btn-outline-secondary" data-id="${t.transferID}" data-action="view">
                        <i class="fa-solid fa-eye me-1"></i> Xem
                      </button>`;
    }

    return `
      <tr>
        <td><strong>#${t.transferID}</strong></td>
        <td>${esc(t.storeName)}</td> 
        <td class="text-center">${getStatusBadge(t.status)}</td>
        <td>${new Date(t.submittedAt).toLocaleString('vi-VN')}</td>
        <td class="text-end">
          ${actionButton}
        </td>
      </tr>
    `;
  }).join('');

  tbody.querySelectorAll('button[data-id]').forEach(btn => {
    btn.addEventListener('click', handleActionClick);
  });
}

// ====== Xử lý Modal (Xem/Duyệt) ======

async function handleActionClick(e) {
  const btn = e.currentTarget;
  const id = btn.dataset.id;
  const action = btn.dataset.action;
  
  CURRENT_MODAL_ID = id; 
  el('actionWrap').style.display = (action === 'process') ? 'block' : 'none';
  
  try {
    // Gọi API Detail (Backend trả về PickList theo FEFO)
    const data = await api(`/api/warehouse/transfers/${id}`); 
    if (!data) throw new Error('Không tìm thấy dữ liệu phiếu.');
    
    el('mId').textContent = data.transferID;
    el('mStatus').outerHTML = `<span id="mStatus">${getStatusBadge(data.status)}</span>`; 
    el('mFrom').value = esc(data.fromLocationName); 
    el('mTo').value = esc(data.toLocationName);
    el('mCreatedBy').value = esc(data.createdByName || `User ID: ${data.createdBy}`);
    
    // Lưu PickList để dùng khi bấm Chấp nhận
    CURRENT_MODAL_ITEMS = data.pickList || [];
    renderModalItems();
    
    MODAL_INSTANCE.show();
    
  } catch (e) {
    alert('Lỗi tải dữ liệu: ' + e.message);
  }
}

function renderModalItems() {
  const tbody = el('mTbody');
  if (!CURRENT_MODAL_ITEMS || CURRENT_MODAL_ITEMS.length === 0) {
    tbody.innerHTML = `<tr><td colspan="5" class="text-center text-muted p-3">Không có dữ liệu xuất kho.</td></tr>`;
    return;
  }
  
  tbody.innerHTML = CURRENT_MODAL_ITEMS.map((item) => {
    // Format ngày hết hạn
    const expiryDate = item.expiryDate ? new Date(item.expiryDate).toLocaleDateString('vi-VN') : '---';
    
    // Nếu là dòng báo thiếu hàng (Backend trả về flag isMissing)
    if (item.isMissing) {
        return `
        <tr class="table-danger">
            <td>
                <div class="fw-semibold">${esc(item.name)}</div>
                <div class="small">SKU: ${esc(item.sku)}</div>
            </td>
            <td>${esc(item.barcode)}</td>
            <td colspan="2" class="text-danger fw-bold text-center align-middle">
                <i class="fa-solid fa-triangle-exclamation me-1"></i> KHÔNG ĐỦ HÀNG TRONG KHO
            </td>
            <td class="text-center fw-bold text-danger align-middle">${item.pickQty}</td>
        </tr>`;
    }

    // Dòng bình thường (có Batch)
    return `
    <tr>
      <td>
        <div class="fw-semibold">${esc(item.name)}</div>
        <div class="small text-muted">SKU: ${esc(item.sku)}</div>
      </td>
      <td>${esc(item.barcode)}</td>
      <td>
        <span class="badge bg-light text-dark border">
            ${esc(item.batchNo)} <span class="text-muted ms-1">(#${item.batchID})</span>
        </span>
      </td>
      <td>${expiryDate}</td>
      <td class="text-center fw-bold text-primary fs-6">${item.pickQty}</td>
    </tr>
  `}).join('');
}

// ====== Gắn sự kiện cho nút "Chấp nhận & Xuất kho" ======
async function handleAccept() {
    
    // Kiểm tra xem có dòng nào bị thiếu hàng không
    const hasMissing = CURRENT_MODAL_ITEMS.some(i => i.isMissing);
    if (hasMissing) {
        alert('Lỗi: Kho không đủ hàng để đáp ứng phiếu này (xem các dòng màu đỏ).\nVui lòng kiểm tra lại tồn kho hoặc yêu cầu Store hủy phiếu.');
        return;
    }
    
    if (!confirm('Hệ thống đã tự động chọn lô theo nguyên tắc FEFO (Hết hạn trước - Xuất trước).\n\nBạn có chắc chắn muốn XUẤT KHO theo kế hoạch này?')) {
        return;
    }
    
    // Build body từ PickList (Backend cần list này để trừ kho)
    const lines = CURRENT_MODAL_ITEMS.map(item => ({
        goodId: item.goodID,
        batchId: item.batchID,
        shipQty: item.pickQty
    }));
    
    const body = { Lines: lines };
    
    try {
        // API POST /accept (Logic 1-bước)
        await api(`/api/warehouse/transfers/${CURRENT_MODAL_ID}/accept`, 'POST', body);
        
        alert('Xuất kho thành công! Hàng đã được chuyển sang Store.');
        MODAL_INSTANCE.hide();
        loadTransfers(); // Tải lại danh sách
        
    } catch(e) {
        alert('Lỗi khi xuất kho: ' + e.message);
    }
}

// ====== Gắn sự kiện cho nút "Từ chối" (Hàm Mới) ======
async function handleReject() {
    if (!confirm('Bạn có chắc chắn muốn TỪ CHỐI (HỦY) phiếu này không?\n\nHàng đã đặt giữ (Reserved) sẽ được trả lại cho kho.')) {
        return;
    }
    
    try {
        // API POST /api/warehouse/transfers/{id}/reject
        await api(`/api/warehouse/transfers/${CURRENT_MODAL_ID}/reject`, 'POST');
        
        alert('Đã từ chối phiếu thành công. Phiếu đã chuyển sang trạng thái "Cancelled".');
        MODAL_INSTANCE.hide();
        loadTransfers(); // Tải lại danh sách
        
    } catch(e) {
        alert('Lỗi khi từ chối phiếu: ' + e.message);
    }
}