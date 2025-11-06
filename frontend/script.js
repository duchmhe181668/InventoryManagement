// ===== User Manager (Admin only) — Danh sách + Thêm + Sửa + Xoá =====
// API base cố định theo yêu cầu:
let API_BASE = 'https://localhost:7225/api/Auth';
API_BASE = API_BASE.replace(/\/+$/,''); // bỏ dấu / cuối

// ===== Endpoints =====
const URL = {
  list:     () => `${API_BASE}/users`,
  byId:     (id) => `${API_BASE}/user/${id}`,
  register: () => `${API_BASE}/register`,        // Backend đang mở AllowAnonymous; dùng cho tạo user
  update:   (id) => `${API_BASE}/update/${id}`,  // [Authorize(Roles="Administrator")]
  del:      (id) => `${API_BASE}/delete/${id}`,  // [Authorize(Roles="Administrator")]
};

// ===== DOM =====
const tbody      = document.getElementById('userTbody');
const emptyState = document.getElementById('emptyState');
const alertBox   = document.getElementById('alertBox');

const addBtn   = document.getElementById('addBtn');
const addForm  = document.getElementById('addForm');
const editForm = document.getElementById('editForm');
const delForm  = document.getElementById('delForm');
const delName  = document.getElementById('delName');

const addModal  = new bootstrap.Modal(document.getElementById('addModal'));
const editModal = new bootstrap.Modal(document.getElementById('editModal'));
const delModal  = new bootstrap.Modal(document.getElementById('delModal'));

// ===== UI helpers =====
function showAlert(msg, type='danger', sticky=true) {
  if (!alertBox) return;
  alertBox.textContent = msg;
  alertBox.className = `alert alert-${type}`;
  alertBox.classList.remove('d-none');
  if (!sticky) setTimeout(() => alertBox.classList.add('d-none'), 3500);
}
function clearAlert() {
  if (!alertBox) return;
  alertBox.className = 'alert d-none';
  alertBox.textContent = '';
}
function setEmptyState(show) {
  emptyState?.classList.toggle('d-none', !show);
}
function escapeHtml(s) {
  return String(s ?? '')
    .replaceAll('&','&amp;').replaceAll('<','&lt;')
    .replaceAll('>','&gt;').replaceAll('"','&quot;')
    .replaceAll("'",'&#39;');
}

// ===== Token & Auth =====
function getStore() {
  // Ưu tiên sessionStorage (nếu login KHÔNG tick “Ghi nhớ”), sau đó mới tới localStorage
  if (sessionStorage.getItem('accessToken') || sessionStorage.getItem('authToken')) return sessionStorage;
  if (localStorage.getItem('accessToken') || localStorage.getItem('authToken'))     return localStorage;
  return localStorage;
}
function getToken() {
  const store = getStore();
  const token = store.getItem('accessToken') || store.getItem('authToken') || '';
  const type  = store.getItem('tokenType') || 'Bearer';
  return { token, type, store, has: !!token };
}
// Fallback: decode JWT 'exp' (seconds) → ms
function getJwtExpMs(token) {
  try {
    const parts = token.split('.');
    if (parts.length < 2) return null;
    const payload = JSON.parse(atob(parts[1].replace(/-/g,'+').replace(/_/g,'/')));
    if (!payload || typeof payload.exp !== 'number') return null;
    return payload.exp * 1000;
  } catch { return null; }
}
function parseExpireAt(raw) {
  if (!raw) return null;
  let n = Number(raw);
  if (!Number.isFinite(n)) {
    const t = Date.parse(raw);
    if (Number.isFinite(t)) n = t;
  }
  if (!Number.isFinite(n)) return null;
  if (n < 1e12) n *= 1000; // nếu là giây → ms
  return n;
}
function checkTokenExpiry() {
  const { token, store } = getToken();
  let expireAt = parseExpireAt(store.getItem('tokenExpireAt'));
  if (!expireAt && token) expireAt = getJwtExpMs(token); // fallback từ JWT exp
  if (!expireAt) return true; // không biết hạn → cho qua
  return Date.now() <= (expireAt + 5000); // tolerance 5s
}
function ensureAuthOrRedirect() {
  const { has } = getToken();
  const valid = checkTokenExpiry();
  if (!has || !valid) {
    showAlert('Phiên đăng nhập đã hết hạn hoặc thiếu token. Vui lòng đăng nhập lại.', 'warning');
    setTimeout(() => (window.location.href = 'login.html'), 800);
    return false;
  }
  return true;
}
function buildHeaders(isJsonBody = false, extra = {}) {
  const { token, type } = getToken();
  if (!token) throw new Error('Thiếu token');
  const base = { 'Accept':'application/json', 'Authorization': `${type} ${token}` };
  return Object.assign({}, base, isJsonBody ? { 'Content-Type':'application/json' } : {}, extra || {});
}
async function fetchWithAuth(url, options = {}) {
  const opts = { ...options };
  const isJsonBody = opts.body && typeof opts.body !== 'string';
  // TỰ set Content-Type và stringify để tránh 415
  if (isJsonBody) opts.body = JSON.stringify(opts.body);
  opts.headers = buildHeaders(isJsonBody, opts.headers);

  const res = await fetch(url, opts);

  if (res.status === 401) {
    showAlert('Phiên đăng nhập không hợp lệ (401). Đang chuyển về trang đăng nhập…', 'warning');
    setTimeout(() => (window.location.href = 'login.html'), 800);
    throw new Error('401 Unauthorized');
  }
  if (res.status === 403) {
    // Không đủ quyền (không phải Admin) — không chuyển trang, chỉ cảnh báo
    showAlert('Bạn không có quyền thực hiện thao tác này (chỉ Administrator).', 'warning');
    throw new Error('403 Forbidden');
  }

  const text = await res.text().catch(()=> '');
  let data;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }

  if (!res.ok) {
    const msg = (data && (data.title || data.message)) || (typeof data === 'string' ? data : `HTTP ${res.status}`);
    throw new Error(msg);
  }
  return data;
}

// ===== Role helpers (ẩn UI nếu không phải Admin) =====
function getRoleLower() {
  const store = getStore();
  const role = store.getItem('authRole');
  if (role) return role.toLowerCase();

  // Fallback: thử đọc role từ JWT payload (các claim: "role" hoặc ClaimTypes.Role)
  const { token } = getToken();
  if (!token) return '';
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g,'+').replace(/_/g,'/')));
    const raw = payload['role'] || payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || '';
    if (Array.isArray(raw)) return (raw[0] || '').toString().toLowerCase();
    return (raw || '').toString().toLowerCase();
  } catch { return ''; }
}
function hideAdminUiIfNeeded(){
  const isAdmin = getRoleLower() === 'administrator';
  document.querySelectorAll('[data-admin-only]').forEach(el => {
    el.classList.toggle('d-none', !isAdmin);
    el.toggleAttribute('disabled', !isAdmin);
  });
  return isAdmin;
}

// ===== Misc helpers =====
function pick(obj, ...keys) {
  for (const k of keys) if (obj && obj[k] !== undefined && obj[k] !== null) return obj[k];
  return undefined;
}
function isAdminRole(roleName) {
  return String(roleName || '').trim().toLowerCase().includes('admin');
}
function normalizeRole(u) {
  const roleField = pick(u, 'RoleName', 'roleName', 'Role', 'role');
  if (!roleField) return '';
  if (typeof roleField === 'string') return roleField;
  return pick(roleField, 'roleName', 'RoleName', 'name', 'Name') || '';
}
// Unwrap kiểu trả về mảng
function unwrapList(data) {
  if (Array.isArray(data)) return data;
  if (data && Array.isArray(data.$values)) return data.$values;
  if (data && Array.isArray(data.data))     return data.data;
  if (data && Array.isArray(data.items))    return data.items;
  return [];
}

// ===== LOAD LIST =====
async function loadUsers() {
  clearAlert();
  if (!ensureAuthOrRedirect()) return;

  try {
    const data = await fetchWithAuth(URL.list(), { method: 'GET' });
    const list = unwrapList(data);
    render(list);
    setEmptyState(list.length === 0);
    if (list.length === 0) showAlert('Không có người dùng nào.', 'info', false);
  } catch (e) {
    render([]);
    setEmptyState(true);
    // showAlert đã hiển thị phù hợp trong fetchWithAuth (401/403),
    // ở đây thêm chi tiết khi là lỗi khác:
    if (!/401|403/.test(String(e))) {
      showAlert('Lỗi khi tải danh sách người dùng: ' + (e.message || e), 'danger', true);
    }
  }
}

function render(list) {
  tbody.innerHTML = '';
  if (!list || list.length === 0) return;

  const frag = document.createDocumentFragment();
  for (const u of list) {
    const id    = pick(u, 'UserID', 'userID', 'userId', 'id') ?? '';
    const usern = pick(u, 'Username', 'username') ?? '';
    const name  = pick(u, 'Name', 'name') ?? '';
    const email = pick(u, 'Email', 'email') ?? '';
    const phone = pick(u, 'PhoneNumber', 'phoneNumber', 'phone') ?? '';
    const role  = normalizeRole(u);
    const adminTarget = isAdminRole(role);

    const actionsHtml = adminTarget
      ? '<span class="badge bg-secondary" title="Admin user is protected">Admin</span>'
      : `
        <button class="btn btn-sm btn-outline-primary me-1" data-action="edit" data-admin-only data-id="${id}">
          <i class="bi bi-pencil"></i>
        </button>
        <button class="btn btn-sm btn-outline-danger" data-action="delete" data-admin-only data-id="${id}"
                data-name="${escapeHtml(usern || name || ('#'+id))}">
          <i class="bi bi-trash"></i>
        </button>`;

    const tr = document.createElement('tr');
    tr.dataset.role = role || ''; // để guard ở click handler
    tr.innerHTML = `
      <td>${id}</td>
      <td>${escapeHtml(usern)}</td>
      <td>${escapeHtml(name)}</td>
      <td>${escapeHtml(email)}</td>
      <td>${escapeHtml(phone)}</td>
      <td>${escapeHtml(role)}</td>
      <td class="text-end">${actionsHtml}</td>
    `;
    frag.appendChild(tr);
  }
  tbody.appendChild(frag);

  // Ẩn nút nếu người XEM không phải admin (giữ behavior cũ)
  hideAdminUiIfNeeded();
}


// ===== ADD =====
addBtn?.addEventListener('click', () => {
  addForm.reset();
  clearAlert();
  addModal.show();
});

addForm?.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearAlert();
  if (!ensureAuthOrRedirect()) return;

  const f = addForm;
  const payload = {
    Username:    f.Username.value.trim(),
    Password:    f.Password.value,
    Name:        f.Name.value.trim(),
    Email:       f.Email.value.trim()           || null,
    PhoneNumber: f.PhoneNumber.value.trim()     || null,
    RoleName:    f.RoleName.value.trim()        || 'Supplier'
  };
  if (!payload.Username || !payload.Password || !payload.Name) {
    return showAlert('Vui lòng nhập đủ Username, Password, Name.', 'warning', false);
  }
  try {
    await fetchWithAuth(URL.register(), { method: 'POST', body: payload });
    addModal.hide();
    showAlert('Tạo người dùng thành công!', 'success', false);
    loadUsers();
  } catch (e2) {
    // 403 sẽ được bắt sẵn trong fetchWithAuth
    if (!/401|403/.test(String(e2))) showAlert('Tạo thất bại: ' + e2.message, 'danger', true);
  }
});

// ===== ROW ACTIONS =====
tbody.addEventListener('click', async (e) => {
  const btn = e.target.closest('button[data-action]');
  if (!btn) return;
  const id = btn.getAttribute('data-id');

  if (btn.dataset.action === 'edit') {
    if (!ensureAuthOrRedirect()) return;
    try {
      const u = await fetchWithAuth(URL.byId(id), { method: 'GET' });
      editForm.UserID.value      = pick(u,'UserID','userID','userId','id') ?? '';
      editForm.Username.value    = pick(u,'Username','username') ?? '';
      editForm.Name.value        = pick(u,'Name','name') ?? '';
      editForm.Email.value       = pick(u,'Email','email') ?? '';
      editForm.PhoneNumber.value = pick(u,'PhoneNumber','phoneNumber','phone') ?? '';
      editForm.RoleName.value    = normalizeRole(u);
      clearAlert();
      editModal.show();
    } catch (err) {
      if (!/401|403/.test(String(err))) showAlert('Không tải được thông tin người dùng: ' + err.message, 'danger', true);
    }
  }

  if (btn.dataset.action === 'delete') {
    delForm.UserID.value = id;
    delName.textContent  = btn.getAttribute('data-name') || ('#' + id);
    clearAlert();
    delModal.show();
  }
});

// ===== UPDATE =====
editForm?.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearAlert();
  if (!ensureAuthOrRedirect()) return;

  const f = editForm;
  const id = f.UserID.value;

  const payload = {};
  if (f.Username.value.trim())    payload.Username    = f.Username.value.trim();
  if (f.Name.value.trim())        payload.Name        = f.Name.value.trim();
  if (f.Email.value.trim())       payload.Email       = f.Email.value.trim();
  if (f.PhoneNumber.value.trim()) payload.PhoneNumber = f.PhoneNumber.value.trim();
  if (f.RoleName.value.trim())    payload.RoleName    = f.RoleName.value.trim();

  try {
    await fetchWithAuth(URL.update(id), { method: 'PUT', body: payload });
    editModal.hide();
    showAlert('Cập nhật thành công!', 'success', false);
    loadUsers();
  } catch (e2) {
    if (!/401|403/.test(String(e2))) showAlert('Cập nhật thất bại: ' + e2.message, 'danger', true);
  }
});

// ===== DELETE =====
delForm?.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearAlert();
  if (!ensureAuthOrRedirect()) return;

  const id = delForm.UserID.value;
  try {
    await fetchWithAuth(URL.del(id), { method: 'DELETE' });
    delModal.hide();
    showAlert('Đã xoá người dùng!', 'success', false);
    loadUsers();
  } catch (e2) {
    if (!/401|403/.test(String(e2))) showAlert('Xoá thất bại: ' + e2.message, 'danger', true);
  }
});

// ===== INIT =====
document.addEventListener('DOMContentLoaded', () => {
  if (!ensureAuthOrRedirect()) return;

  const isAdmin = hideAdminUiIfNeeded();
  if (!isAdmin) {
    // Không gọi API để tránh spam 403; chỉ báo quyền hạn
    showAlert('Trang này chỉ dành cho Administrator.', 'warning');
    setEmptyState(true);
    return;
  }
  loadUsers();
});
