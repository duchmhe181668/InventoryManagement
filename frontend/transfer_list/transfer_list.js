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

/**
 * Kiểm tra xác thực và phân quyền.
 */
function checkAuth(allowedRoles = []) {
  const token = localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken');
  if (!token) {
    alert("Bạn chưa đăng nhập hoặc phiên đã hết hạn. Vui lòng đăng nhập lại.");
    window.location.href = '../login.html'; // Điều hướng về trang login
    return { token: null, hasPermission: false, role: null };
  }
  
  let userRole = localStorage.getItem('authRole') || sessionStorage.getItem('authRole');
  
  if (!userRole) {
    const payload = parseJwt(token);
    userRole = payload ? (payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '') : '';
  }
  
  const roleToCompare = (userRole || '').toLowerCase();

  // Nếu trang này có yêu cầu phân quyền
  if (allowedRoles.length > 0) {
      if (!allowedRoles.includes(roleToCompare)) {
          alert("Bạn không có quyền truy cập chức năng này.");
          window.location.href = '../home.html'; // Điều hướng về trang chủ
          return { token, hasPermission: false, role: roleToCompare };
      }
  }
  
  // Đã đăng nhập và có quyền
  return { token, hasPermission: true, role: roleToCompare };
}
// ====== Config ======
const API_BASE = 'https://localhost:7225';
let AUTH_TOKEN = null;

// ====== Utils ======
const el = id => document.getElementById(id);
const esc = s => (s ?? '').toString().replace(/[&<>"'\\]/g, m => ({
  '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;','\\':'&#92;'
}[m]));

// Dùng CSS class từ file purchase_order_list.css
function getStatusBadge(status) {
    const s = (status || 'draft').toLowerCase();
    const map = {
        'draft': 'status-draft',
        'approved': 'status-success',
        'receiving': 'status-warning',
        'received': 'status-success',
        'cancelled': 'status-secondary'
    };
    const defaultClass = 'status-info'; // Dùng cho 'Submitted'
    const cssClass = map[s] || defaultClass;
    return `<span class="badge ${cssClass}">${esc(status)}</span>`;
}


// ====== Boot ======
window.addEventListener('DOMContentLoaded', init);

function init() {
  // 1) Auth
  const auth = checkAuth(['storemanager', 'administrator']); 
  if (!auth.hasPermission) return;
  AUTH_TOKEN = auth.token;

  // 2) Gắn sự kiện
  el('btnReload').addEventListener('click', loadTransfers);
  el('status').addEventListener('change', loadTransfers);

  // 3) Tải danh sách
  loadTransfers();
}

// ====== API helper ======
async function api(path, method = 'GET', body = null) {
  const resp = await fetch(`${API_BASE}${path}`, { // Sửa lỗi 404: API_BASE không chứa /api
    method,
    headers: {
      'Authorization': `Bearer ${AUTH_TOKEN}`,
      ...(body ? { 'Content-Type': 'application/json' } : {})
    },
    body: body ? JSON.stringify(body) : null
  });

  if (!resp.ok) {
    if (resp.status === 401 || resp.status === 403) checkAuth(['storemanager', 'administrator']);
    const errText = await resp.text();
    throw new Error(errText || `HTTP ${resp.status}`);
  }
  try {
    const text = await resp.text();
    return text ? JSON.parse(text) : null;
  } catch (e) {
    return null;
  }
}

async function loadTransfers() {
  const status = el('status').value;
  const tbody = el('tbody');
  tbody.innerHTML = `<tr><td colspan="6" class="text-center p-4"><div class="spinner-border spinner-border-sm"></div> Đang tải...</td></tr>`;

  try {
    // API /api/transfers (ListTransfers) đã được sửa ở Backend
    const transfers = await api(`/api/transfers?status=${encodeURIComponent(status)}`);
    renderTable(transfers);
  } 
  catch (e) {
    tbody.innerHTML = `<tr><td colspan="6" class="text-center text-danger p-4">Lỗi tải dữ liệu: ${e.message}</td></tr>`;
  }
}

function renderTable(transfers) {
  const tbody = el('tbody');
  if (!transfers || transfers.length === 0) {
    tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted p-4">Không tìm thấy phiếu nào.</td></tr>`;
    return;
  }

  tbody.innerHTML = transfers.map(t => {
    // API trả về Id, fromName, toName, status, createdAt
    const isDraft = (t.status || '').toLowerCase() === 'draft';
    
    // === SỬA ĐỔI: Dùng <a> (thẻ link) thay vì <button> ===
    const pageUrl = '../transfer/transfer.html'; // Đường dẫn tới trang tạo/sửa
    let actionButton = '';

    if (isDraft) {
      // Nếu là Draft, cho Sửa
      actionButton = `<a href="${pageUrl}?id=${t.id}&edit=true" class="btn btn-sm btn-outline-primary">
                        <i class="fa-solid fa-pencil me-1"></i> Sửa
                      </a>`;
    } else {
      // Nếu đã Submit/Approved..., chỉ cho Xem
      actionButton = `<a href="${pageUrl}?id=${t.id}" class="btn btn-sm btn-outline-secondary">
                        <i class="fa-solid fa-eye me-1"></i> Xem
                      </a>`;
    }

    return `
      <tr>
        <td><strong>#${t.id}</strong></td>
        <td>${esc(t.fromName)}</td>
        <td>${esc(t.toName)}</td>
        <td class="text-center">${getStatusBadge(t.status)}</td>
        <td>${new Date(t.createdAt).toLocaleString('vi-VN')}</td>
        <td class="text-end">
          ${actionButton} </td>
      </tr>
    `;
  }).join('');
  
}

