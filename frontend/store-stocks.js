// ===== Helpers =====
const $  = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const fmt = v => (v ?? 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });

// API base
function getApiBase() {
  return localStorage.getItem('apiBase') || 'https://localhost:7225';
}
function setApiBase(v) {
  localStorage.setItem('apiBase', v);
  $('#api-base').value = v;
}
$('#api-base').value = getApiBase();
$('#btn-set-api').addEventListener('click', () =>
  setApiBase($('#api-base').value.trim() || 'https://localhost:7225')
);

// ===== Auth helpers (copy từ store-goods.js) =====
function parseJwt(token) {
  try {
    const base64Url = token.split('.')[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map(c => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(jsonPayload);
  } catch {
    return null;
  }
}

function getAuthPayload() {
  const raw = localStorage.getItem('authUser') || sessionStorage.getItem('authUser');
  if (raw) {
    try { return JSON.parse(raw); } catch {}
  }

  const accessToken =
    localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken') ||
    localStorage.getItem('authToken')   || sessionStorage.getItem('authToken')   || '';
  const tokenType =
    localStorage.getItem('tokenType')   || sessionStorage.getItem('tokenType')   || 'Bearer';
  const storeIdStr =
    localStorage.getItem('storeId')     || sessionStorage.getItem('storeId');

  const payload = { accessToken, tokenType };
  if (storeIdStr != null && storeIdStr !== '') payload.storeId = Number(storeIdStr);

  // nếu chưa có storeId thì thử đọc từ JWT
  if (accessToken && payload.storeId == null) {
    const claims = parseJwt(accessToken) || {};
    const keys = ['storeId', 'store_id', 'sid', 'store', 'StoreId', 'StoreID'];
    for (const k of keys) {
      if (claims[k] != null && claims[k] !== '') {
        const v = Number(claims[k]);
        if (!Number.isNaN(v)) {
          payload.storeId = v;
          try { sessionStorage.setItem('storeId', String(v)); } catch {}
          break;
        }
      }
    }
  }
  return accessToken ? payload : null;
}

function getStoreId() {
  const v = getAuthPayload()?.storeId;
  return typeof v === 'number' && !Number.isNaN(v) ? v : null;
}

// fetch JSON kèm Authorization nếu có token
async function fetchJSON(url) {
  const auth = getAuthPayload();
  const headers = {};
  if (auth?.accessToken) {
    headers['Authorization'] = `${auth.tokenType || 'Bearer'} ${auth.accessToken}`;
  }
  const r = await fetch(url, { headers });
  if (!r.ok) throw new Error(await r.text());
  return r.json();
}

// ===== State & pager =====
const state = {
  page: 1,
  pageSize: 20,
  totalPages: 1,
  totalItems: 0
};

function buildPager() {
  const ul = $('#pager'); ul.innerHTML = '';
  const mk = (p, txt, active = false, disabled = false) => {
    const li = document.createElement('li');
    li.className = 'page-item' + (active ? ' active' : '') + (disabled ? ' disabled' : '');
    const a = document.createElement('a');
    a.className = 'page-link';
    a.href = '#';
    a.textContent = txt;
    a.addEventListener('click', e => {
      e.preventDefault();
      if (!disabled) {
        state.page = p;
        load();
      }
    });
    li.appendChild(a);
    ul.appendChild(li);
  };
  mk(Math.max(1, state.page - 1), '«', false, state.page <= 1);
  for (let p = 1; p <= state.totalPages; p++) {
    if (p === 1 || p === state.totalPages || Math.abs(p - state.page) <= 2) {
      mk(p, String(p), p === state.page);
    } else if (Math.abs(p - state.page) === 3) {
      const li = document.createElement('li');
      li.className = 'page-item disabled';
      li.innerHTML = '<span class="page-link">…</span>';
      ul.appendChild(li);
    }
  }
  mk(Math.min(state.totalPages, state.page + 1), '»', false, state.page >= state.totalPages);
}

// ===== Categories & load =====
async function loadCategories() {
  const url = getApiBase() + '/api/Categories';
  const list = await fetchJSON(url);
  const sel = $('#category');
  for (const c of list) {
    const opt = document.createElement('option');
    opt.value = c.categoryID;
    opt.textContent = c.categoryName;
    sel.appendChild(opt);
  }
}

function escapeHtml(s) {
  return String(s ?? '')
    .replaceAll('&', '&amp;').replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;').replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

async function load() {
  const storeId = getStoreId();
  const tbody = $('#tbody');
  const info = $('#resultInfo');
  const storeLbl = $('#storeLabel');

  // Hiển thị store ID ở header
  if (storeLbl) storeLbl.textContent = storeId != null ? `#${storeId}` : 'Không xác định';

  if (!storeId) {
    tbody.innerHTML = `
      <tr>
        <td colspan="7" class="text-center text-danger py-4">
          Không tìm thấy <b>Store ID</b> từ thông tin đăng nhập.
          Vui lòng đăng nhập bằng tài khoản Store Manager.
        </td>
      </tr>`;
    info.textContent = '';
    state.totalPages = 1;
    buildPager();
    return;
  }

  const search = $('#search').value.trim();  //  bỏ encodeURIComponent
const cat = $('#category').value;
const sort = $('#sort').value;
const onlyAvail = $('#onlyAvail').checked;

const qs = new URLSearchParams();
if (search) qs.set('search', search);      // encode
if (cat) qs.set('categoryId', cat);
if (onlyAvail) qs.set('onlyAvailable', 'true');
qs.set('page', state.page);
qs.set('pageSize', state.pageSize);
qs.set('sort', sort);

  if (cat) qs.set('categoryId', cat);
  if (onlyAvail) qs.set('onlyAvailable', 'true');
  qs.set('page', state.page);
  qs.set('pageSize', state.pageSize);
  qs.set('sort', sort);

  const url = `${getApiBase()}/api/stocks/store/${storeId}?${qs.toString()}`;

  try {
    const res = await fetchJSON(url);
    state.page = res.page;
    state.pageSize = res.pageSize;
    state.totalPages = res.totalPages;
    state.totalItems = res.totalItems;

    tbody.innerHTML = '';
    for (const it of res.items) {
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td><code>${it.sku}</code></td>
        <td>${escapeHtml(it.goodName)}</td>
        <td>${escapeHtml(it.categoryName ?? '')}</td>
        <td class="text-end">${fmt(it.onHand)}</td>
        <td class="text-end">${fmt(it.reserved)}</td>
        <td class="text-end">${fmt(it.inTransit)}</td>
        <td class="text-end fw-semibold">${fmt(it.available)}</td>
      `;
      tbody.appendChild(tr);
    }

    info.textContent =
      `Trang ${state.page}/${state.totalPages} — ` +
      `${state.totalItems.toLocaleString('vi-VN')} mặt hàng`;
    buildPager();
  } catch (err) {
    console.error(err);
    tbody.innerHTML = `
      <tr>
        <td colspan="7" class="text-center text-danger py-4">
          Không tải được dữ liệu stocks (${err.message})
        </td>
      </tr>`;
    info.textContent = '';
    state.totalPages = 1;
    buildPager();
  }
}

// ===== Events =====
$('#btn-refresh').addEventListener('click', () => { state.page = 1; load(); });
$('#search').addEventListener('keydown', e => {
  if (e.key === 'Enter') { state.page = 1; load(); }
});
$('#category').addEventListener('change', () => { state.page = 1; load(); });
$('#sort').addEventListener('change', () => { state.page = 1; load(); });
$('#onlyAvail').addEventListener('change', () => { state.page = 1; load(); });

// Init
(async function init() {
  try {
    await loadCategories();
    await load();
  } catch (err) {
    console.error(err);
    alert('Lỗi tải dữ liệu: ' + err.message);
  }
})();
