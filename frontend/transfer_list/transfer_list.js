
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
   BẮT ĐẦU CODE CỦA TRANG LIST
   ========================= */

const API_BASE = 'https://localhost:7225';
let AUTH_TOKEN = null;

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
  const auth = checkAuth(['storemanager', 'administrator']); 
  if (!auth.hasPermission) return;
  AUTH_TOKEN = auth.token;
  
  el('status').addEventListener('change', loadTransfers);

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
    if (resp.status === 401 || resp.status === 403) checkAuth(['storemanager', 'administrator']);
    
    let errText = `Lỗi HTTP ${resp.status}`;
    try {
        const errData = await resp.json();
        errText = errData.detail || errData.title || errData.message || JSON.stringify(errData);
    } catch(e) {
        errText = await resp.text() || errText;
    }
    
    throw new Error(errText);
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
  tbody.innerHTML = `<tr><td colspan="6" class="text-center p-4"><div class="spinner-border spinner-border-sm"></div> Đang tải...</td></tr>`;

  try {
    // API /api/transfers (ListTransfers) vẫn giữ nguyên
    const transfers = await api(`/api/transfers?status=${encodeURIComponent(status)}`);
    renderTable(transfers);
  } catch (e) {
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
    const isDraft = (t.status || '').toLowerCase() === 'draft';
    
    const pageUrl = '../transfer/transfer.html'; 
    let actionButton = '';

    if (isDraft) {
      actionButton = `<a href="${pageUrl}?id=${t.id}&edit=true" class="btn btn-sm btn-outline-primary">
                        <i class="fa-solid fa-pencil me-1"></i> Sửa
                      </a>`;
    } else {
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
          ${actionButton}
        </td>
      </tr>
    `;
  }).join('');
}