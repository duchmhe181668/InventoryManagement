// ===== Config =====
const API_GOODS = 'https://localhost:7225/api/Goods';
const API_CATES = 'https://localhost:7225/api/Categories';
const PLACEHOLDER_IMG = 'data:image/svg+xml;charset=UTF-8,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%22100%22%20height%3D%22100%22%20viewBox%3D%220%200%20100%20100%22%3E%3Crect%20width%3D%22100%22%20height%3D%22100%22%20fill%3D%22%23cccccc%22%2F%3E%3Ctext%20x%3D%2250%25%22%20y%3D%2250%25%22%20font-size%3D%2230%22%20fill%3D%22%23333333%22%20text-anchor%3D%22middle%22%20dominant-baseline%3D%22central%22%3E%3F%3C%2Ftext%3E%3C%2Fsvg%3E';

// ===== State =====
const state = { page: 1, pageSize: 20, sort: 'name', search: '' };

// ===== DOM =====
const $ = (sel, root=document) => root.querySelector(sel);
const $$ = (sel, root=document) => [...root.querySelectorAll(sel)];
const tbody = $('#tbody');
const pageInfo = $('#page-info');
const btnPrev = $('#btn-prev');
const btnNext = $('#btn-next');
const searchInput = $('#search-input');
const btnAdd = $('#btn-add');
const btnLogout = $('#btn-logout');

// Modal + form
const modalEl = $('#goodModal');
const modal = new bootstrap.Modal(modalEl);
const form = $('#good-form');
const modalTitle = $('#modal-title');
const formMsg = $('#form-msg');
const btnDelete = $('#btn-delete');

// Fields
const f = {
  id: $('#goodId'),
  rowVersion: $('#rowVersion'),
  currentCateId: $('#currentCategoryId'),
  name: $('#name'),
  sku: $('#sku'),
  priceCost: $('#priceCost'),
  priceSell: $('#priceSell'),
  unit: $('#unit'),
  barcode: $('#barcode'),
  imageURL: $('#imageURL'),
  cateName: $('#categoryName'),
  cateList: $('#categories')
};

// ===== Utils =====
const fmtCurrency = v =>
  (v ?? '') === '' ? 'N/A' :
  new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' })
    .format(v).replace('₫','').trim();

const setLoading = (msg='Đang tải dữ liệu...') => {
  tbody.innerHTML = `<tr><td colspan="8" class="text-center py-4"><i class="fa-solid fa-spinner fa-spin me-1"></i>${msg}</td></tr>`;
};

const toastMsg = (msg, ok=true) => {
  formMsg.textContent = msg;
  formMsg.className = `text-end small mt-2 ${ok ? 'text-success' : 'text-danger'}`;
};

// ===== Category cache =====
const cateMap = new Map();

async function loadCategories() {
  try {
    const res = await fetch(API_CATES);
    if (!res.ok) throw 0;
    const data = await res.json();
    cateMap.clear(); f.cateList.innerHTML='';
    data.forEach(c => {
      const name = (c.categoryName||'').trim();
      if (!name) return;
      cateMap.set(name, c.categoryID);
      const opt = document.createElement('option');
      opt.value = name; f.cateList.appendChild(opt);
    });
    f.cateName.placeholder = 'Nhập tên danh mục hoặc chọn';
  } catch {
    f.cateName.placeholder = 'Lỗi tải danh mục. Vẫn có thể nhập mới.';
  }
}

async function ensureCategoryId(nameInput) {
  const name = (nameInput||'').trim();
  if (!name) return null;
  if (cateMap.has(name)) return cateMap.get(name);

  // create
  const res = await fetch(API_CATES, {
    method:'POST', headers:{'Content-Type':'application/json'},
    body: JSON.stringify({ categoryName: name })
  });
  if (res.status !== 201) {
    let text = await res.text();
    try { text = JSON.parse(text).title || text; } catch {}
    throw new Error(text || 'Tạo danh mục thất bại');
  }
  const newCate = await res.json();
  cateMap.set(name, newCate.categoryID);
  const opt = document.createElement('option'); opt.value = name; f.cateList.appendChild(opt);
  return newCate.categoryID;
}

// ===== API =====
async function fetchGoods() {
  setLoading();
  const url = new URL(API_GOODS);
  url.searchParams.set('page', state.page);
  url.searchParams.set('pageSize', state.pageSize);
  url.searchParams.set('sort', state.sort);
  if (state.search) url.searchParams.set('search', state.search);

  const res = await fetch(url);
  if (!res.ok) { tbody.innerHTML = `<tr><td colspan="8" class="text-center text-danger py-4">Không thể tải dữ liệu.</td></tr>`; return; }
  const data = await res.json();
  renderTable(data.items);
  updatePaging(data);
}

function renderTable(items=[]) {
  if (!items.length) {
    tbody.innerHTML = `<tr><td colspan="8" class="text-center py-4">Không có dữ liệu.</td></tr>`;
    return;
  }
  tbody.innerHTML = items.map(g => `
    <tr data-id="${g.goodID}" class="row-good">
      <td><img class="goods-img" src="${g.imageURL||PLACEHOLDER_IMG}" onerror="this.src='${PLACEHOLDER_IMG}'" alt=""></td>
      <td>${g.name}</td>
      <td>${g.sku}</td>
      <td>${g.unit}</td>
      <td>${fmtCurrency(g.priceCost)}</td>
      <td class="price-sell">${fmtCurrency(g.priceSell)}</td>
      <td>${g.categoryName||'Không có'}</td>
      <td>
        <button class="btn btn-sm btn-primary btn-edit"><i class="fa-solid fa-pen-to-square"></i> Sửa</button>
      </td>
    </tr>
  `).join('');
}

function updatePaging(p) {
  const totalPages = Math.max(1, p.totalPages || 1);
  const page = Math.min(Math.max(1, p.page||1), totalPages);
  pageInfo.textContent = `Trang ${page} / Tổng ${totalPages} (Số lượng: ${p.totalItems ?? p.total ?? '?'})`;
  btnPrev.disabled = page<=1;
  btnNext.disabled = page>=totalPages;
  state.page = page;
}

// ===== Handlers =====
function onSort(th) {
  const key = th.dataset.sort;
  const curKey = state.sort.replace('-', '');
  const isDesc = state.sort.startsWith('-');
  state.sort = (curKey === key && !isDesc) ? `-${key}` : key;
  $$('.sortable i').forEach(i => i.className = 'fa-solid fa-sort');
  const icon = $('i', th);
  icon.className = state.sort.startsWith('-') ? 'fa-solid fa-sort-down' : 'fa-solid fa-sort-up';
  state.page = 1;
  fetchGoods();
}

async function openCreate() {
  form.reset();
  Object.values(f).forEach(el => el && (el.disabled = false));
  f.id.value=''; f.rowVersion.value=''; f.currentCateId.value=''; f.unit.value='Cái';
  modalTitle.textContent = 'Tạo mới sản phẩm';
  btnDelete.classList.add('d-none');
  formMsg.textContent='';
  modal.show();
}

async function openEdit(id) {
  modalTitle.textContent = 'Đang tải...';
  formMsg.textContent = 'Đang tải dữ liệu sản phẩm...';
  btnDelete.classList.add('d-none');
  modal.show();

  const res = await fetch(`${API_GOODS}/${id}`);
  if (!res.ok) { toastMsg('Không tìm thấy sản phẩm.', false); return; }
  const g = await res.json();

  f.id.value = g.goodID;
  f.rowVersion.value = g.rowVersion ? btoa(String.fromCharCode.apply(null, g.rowVersion)) : '';
  f.name.value = g.name; f.sku.value = g.sku;
  f.priceCost.value = g.priceCost; f.priceSell.value = g.priceSell;
  f.unit.value = g.unit; f.barcode.value = g.barcode||''; f.imageURL.value = g.imageURL||'';
  f.cateName.value = g.categoryName||'';
  f.currentCateId.value = g.categoryID || (g.categoryName && cateMap.has(g.categoryName) ? cateMap.get(g.categoryName) : '');

  modalTitle.textContent = `Cập nhật: ${g.name}`;
  btnDelete.classList.remove('d-none');
  formMsg.textContent='';
  btnDelete.onclick = () => onDelete(g.goodID);
}

async function onSubmit(e) {
  e.preventDefault();
  if (!form.checkValidity()) { form.classList.add('was-validated'); return; }

  try {
    const isEdit = !!f.id.value;
    toastMsg(isEdit ? 'Đang cập nhật...' : 'Đang tạo mới...', true);

    let finalCateId = null;
    const name = f.cateName.value.trim();
    const currentId = f.currentCateId.value;
    if (name) {
      if (isEdit && cateMap.has(name) && String(cateMap.get(name)) === String(currentId)) {
        finalCateId = Number(currentId);
      } else if (cateMap.has(name)) {
        finalCateId = cateMap.get(name);
      } else {
        finalCateId = await ensureCategoryId(name);
      }
    }

    const dto = {
      sku: f.sku.value,
      name: f.name.value,
      unit: f.unit.value,
      barcode: f.barcode.value || null,
      imageURL: f.imageURL.value || null,
      priceCost: parseFloat(f.priceCost.value),
      priceSell: parseFloat(f.priceSell.value),
      categoryId: finalCateId
    };

    if (isEdit && f.rowVersion.value) {
      const bin = atob(f.rowVersion.value);
      dto.rowVersion = Array.from(bin, ch => ch.charCodeAt(0));
    }

    const url = isEdit ? `${API_GOODS}/${f.id.value}` : API_GOODS;
    const method = isEdit ? 'PUT' : 'POST';
    const res = await fetch(url, { method, headers:{'Content-Type':'application/json'}, body: JSON.stringify(dto) });

    if ([200,201,204].includes(res.status)) {
      toastMsg(isEdit ? 'Cập nhật thành công!' : 'Tạo mới thành công!', true);
      setTimeout(() => { modal.hide(); state.page = 1; fetchGoods(); loadCategories(); }, 700);
    } else {
      let text = await res.text(); try { const j=JSON.parse(text); text=j.title||j.Message||text; } catch {}
      if (res.status === 409) text = 'SKU đã tồn tại.';
      if (res.status === 404) text = 'Không tìm thấy sản phẩm.';
      toastMsg(text || `Lỗi ${res.status}`, false);
    }
  } catch (err) {
    toastMsg(err.message || 'Lỗi kết nối.', false);
  }
}

async function onDelete(id) {
  if (!confirm('Xóa sản phẩm này?')) return;
  toastMsg('Đang xóa...', true);
  const res = await fetch(`${API_GOODS}/${id}`, { method:'DELETE' });
  if ([200,204].includes(res.status)) {
    toastMsg('Đã xóa!', true);
    setTimeout(() => { modal.hide(); state.page=1; fetchGoods(); }, 600);
  } else {
    toastMsg('Không thể xóa.', false);
  }
}

function logout() {
  const keys = ['authToken','authUser','supplierId','locationId'];
  keys.forEach(k => localStorage.removeItem(k));
  keys.forEach(k => sessionStorage.removeItem(k));
  location.href = 'login.html';
}

// ===== Wiring =====
function wireEvents() {
  // Sorting
  $$('.sortable').forEach(th => th.addEventListener('click', () => onSort(th)));

  // Search Enter
  searchInput.addEventListener('keypress', e => {
    if (e.key === 'Enter') { state.search = searchInput.value.trim(); state.page = 1; fetchGoods(); }
  });

  // Paging
  btnPrev.addEventListener('click', () => { if (state.page>1){ state.page--; fetchGoods(); } });
  btnNext.addEventListener('click', () => { state.page++; fetchGoods(); });

  // Add
  btnAdd.addEventListener('click', openCreate);

  // Row edit (event delegation)
  tbody.addEventListener('click', e => {
    const btn = e.target.closest('.btn-edit');
    if (!btn) return;
    const tr = e.target.closest('tr');
    openEdit(tr.dataset.id);
  });

  // Submit
  form.addEventListener('submit', onSubmit);

  // Logout
  btnLogout.addEventListener('click', e => { e.preventDefault(); logout(); });
}

// ===== Init =====
document.addEventListener('DOMContentLoaded', () => {
  wireEvents();
  loadCategories().then(()=>{/*noop*/});
  // default sort icon for name
  const nameTh = document.querySelector('th[data-sort="name"] i');
  if (nameTh) nameTh.className = 'fa-solid fa-sort-up';
  fetchGoods();
});
