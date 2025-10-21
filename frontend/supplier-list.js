// ===== Config =====
const API_BASE = 'https://localhost:7225/api';
const API_SUPPLIERS = `${API_BASE}/suppliers`;
const API_POS = (id) => `${API_SUPPLIERS}/${id}/pos`;
const API_RCS = (id) => `${API_SUPPLIERS}/${id}/receipts`;

// ===== Auth Storage helper =====
function getStore(){
  if (sessionStorage.getItem('accessToken') || sessionStorage.getItem('authToken')) return sessionStorage;
  if (localStorage.getItem('accessToken') || localStorage.getItem('authToken')) return localStorage;
  return localStorage;
}
function getToken(){ const s = getStore(); return s.getItem('accessToken') || s.getItem('authToken') || ''; }
function authHeaders(){ const t = getToken(); return t ? { 'Authorization':'Bearer '+t } : {}; }

// ===== Dom =====
const qEl        = document.getElementById('q');
const sortByEl   = document.getElementById('sortBy');
const sortDirEl  = document.getElementById('sortDir');
const pageSizeEl = document.getElementById('pageSize');
const btnRefresh = document.getElementById('btnRefresh');

const tbody      = document.getElementById('tbody');
const pager      = document.getElementById('pager');
const summary    = document.getElementById('summary');
const alertBox   = document.getElementById('alertBox');

// Detail modal elements
const detailModal = new bootstrap.Modal(document.getElementById('detailModal'));
const dName   = document.getElementById('dName');
const dLastPO = document.getElementById('dLastPO');
const dPOCnt  = document.getElementById('dPOCount');
const dRcCnt  = document.getElementById('dRcCount');
const dSpend  = document.getElementById('dSpend');
const poBody  = document.getElementById('poBody');
const rcBody  = document.getElementById('rcBody');

const btnReloadPO = document.getElementById('btnReloadPO');
const btnReloadRC = document.getElementById('btnReloadRC');

// --- Pager DOM (tabs)
const poPager = document.getElementById('poPager');
const poSummary = document.getElementById('poSummary');
const rcPager = document.getElementById('rcPager');
const rcSummary = document.getElementById('rcSummary');

// ===== State =====
const State = { page: 1, pageSize: 10, sortBy: 'name', sortDir: 'desc', q: '' };
const POState = { page: 1, pageSize: 5, statuses: [] };
const RCState = { page: 1, pageSize: 5, statuses: [] };
let currentSupplierId = null;
let lastDetail = null; // dùng cho fallback

// ===== Utils =====
const money = v => (v ?? 0).toLocaleString('vi-VN', {style:'currency', currency:'VND', maximumFractionDigits:0});
const fmt   = (d) => d ? new Date(d).toLocaleString('vi-VN') : '—';
function showAlert(msg, type='danger'){
  alertBox.className = `alert alert-${type}`;
  alertBox.textContent = msg;
  alertBox.classList.remove('d-none');
  setTimeout(()=>alertBox.classList.add('d-none'), 3000);
}
function escapeHtml(str){
  if (str == null) return '';
  return String(str).replaceAll('&','&amp;').replaceAll('<','&lt;')
    .replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'","&#39;");
}

// ===== Fetch main list =====
async function loadSuppliers(){
  tbody.innerHTML = `<tr><td colspan="6" class="text-center py-5"><span class="spinner me-2"></span>Đang tải…</td></tr>`;
  const params = new URLSearchParams({
    page: State.page, pageSize: State.pageSize, sortBy: State.sortBy, sortDir: State.sortDir
  });
  if (State.q.trim()) params.append('q', State.q.trim());

  try{
    const res = await fetch(`${API_SUPPLIERS}?${params.toString()}`, { headers: { 'Content-Type':'application/json', ...authHeaders() }});
    if (!res.ok){
      if (res.status === 401) showAlert('Bạn chưa đăng nhập hoặc token hết hạn.', 'warning');
      else showAlert('Không tải được danh sách supplier.', 'danger');
      tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted py-4">Không có dữ liệu</td></tr>`;
      pager.innerHTML = ''; summary.textContent = ''; return;
    }
    const data = await res.json();
    renderTable(data.items || data.Items || []);
    renderPagerMain(data.totalItems ?? data.TotalItems ?? 0, data.page ?? data.Page ?? 1, data.pageSize ?? data.PageSize ?? State.pageSize);
  }catch(e){ console.error(e); showAlert('Lỗi mạng. Kiểm tra API.', 'danger'); }
}
function renderTable(items){
  if (!items.length){
    tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted py-4">Không có dữ liệu</td></tr>`;
    summary.textContent = '0 supplier'; return;
  }
  tbody.innerHTML = items.map(x => {
    const name = escapeHtml(x.name ?? x.Name);
    const phone = x.phoneNumber ?? x.PhoneNumber ?? '';
    const email = x.email ?? x.Email ?? '';
    const lastPO = x.lastPODate ?? x.LastPODate;
    const poCount = x.poCount ?? x.POCount ?? 0;
    const rcCount = x.receiptCount ?? x.ReceiptCount ?? 0;
    const spend = x.totalSpend ?? x.TotalSpend ?? 0;
    const id = x.supplierID ?? x.SupplierID;
    return `<tr>
      <td><div class="fw-semibold">${name}</div><div class="small text-muted-2">${(x.address ?? x.Address) || ''}</div></td>
      <td><div class="small"><i class="bi bi-telephone me-1"></i>${escapeHtml(phone || '—')}</div>
          <div class="small"><i class="bi bi-envelope me-1"></i>${escapeHtml(email || '—')}</div></td>
      <td class="text-center"><span class="badge badge-soft">${fmt(lastPO)}</span><div class="small text-muted-2">${poCount} PO</div></td>
      <td class="text-center">${rcCount}</td>
      <td class="text-end fw-semibold">${money(spend)}</td>
      <td class="text-end">
        <button class="btn btn-outline-light btn-sm btn-smooth" onclick="openDetail(${id}, '${name.replace(/'/g,"&#39;")}')"><i class="bi bi-eye me-1"></i>Chi tiết</button>
      </td></tr>`;
  }).join('');
  summary.textContent = `${items.length} supplier trong trang hiện tại`;
}
function renderPagerMain(totalItems, page, pageSize){
  State.page = page; State.pageSize = pageSize;
  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));
  const mk = (p, label, active=false, disabled=false) =>
    `<li class="page-item ${active?'active':''} ${disabled?'disabled':''}">
       <a class="page-link clicky" ${disabled?'':`onclick="gotoPage(${p})"`}>${label}</a>
     </li>`;
  let html = '';
  html += mk(Math.max(1, page-1), '&laquo;', false, page===1);
  const win = 3; let start = Math.max(1, page - win), end = Math.min(totalPages, page + win);
  if (page <= win) end = Math.min(totalPages, 1 + 2*win);
  if (page + win >= totalPages) start = Math.max(1, totalPages - 2*win);
  for (let i=start; i<=end; i++) html += mk(i, i, i===page, false);
  html += mk(Math.min(totalPages, page+1), '&raquo;', false, page===totalPages);
  pager.innerHTML = html;
  const from = (page-1)*pageSize + 1, to = Math.min(page*pageSize, totalItems);
  summary.textContent = `${from}-${to} / ${totalItems} supplier`;
}
window.gotoPage = function(p){ State.page = p; loadSuppliers(); };

// ===== Detail (summary + tabs with paging) =====
async function openDetail(id, name){
  currentSupplierId = id;
  dName.textContent = name;
  // reset UI
  dLastPO.textContent = '—'; dPOCnt.textContent = '0'; dRcCnt.textContent = '0'; dSpend.textContent = money(0);
  poBody.innerHTML = `<tr><td colspan="5" class="text-center py-3"><span class="spinner me-2"></span>Đang tải…</td></tr>`;
  rcBody.innerHTML = `<tr><td colspan="6" class="text-center py-3"><span class="spinner me-2"></span>Đang tải…</td></tr>`;
  POState.page = 1; RCState.page = 1;

  // đọc filter hiện tại
  POState.statuses = [...document.querySelectorAll('.po-filter:checked')].map(x=>x.value);
  RCState.statuses = [...document.querySelectorAll('.rc-filter:checked')].map(x=>x.value);

  detailModal.show();
  await loadDetailSummary(); // lấy số liệu tổng
  await Promise.all([loadPOPage(), loadRCPage()]);
}

async function loadDetailSummary(){
  lastDetail = null;
  const params = new URLSearchParams({ max: '1' }); // chỉ cần summary
  try{
    const res = await fetch(`${API_SUPPLIERS}/${currentSupplierId}?${params}`, { headers:{ 'Content-Type':'application/json', ...authHeaders() }});
    if (res.ok){
      const d = await res.json();
      lastDetail = d;
      dLastPO.textContent = fmt(d.lastPODate ?? d.LastPODate);
      dPOCnt.textContent  = d.poCount ?? d.POCount ?? 0;
      dRcCnt.textContent  = d.receiptCount ?? d.ReceiptCount ?? 0;
      dSpend.textContent  = money(d.totalSpend ?? d.TotalSpend ?? 0);
    }
  }catch(e){ console.error(e); }
}

// ---- paging helper for tabs
function renderSubPager(el, totalItems, page, pageSize, goFnName, summaryEl){
  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));
  const mk = (p, lbl, dis=false, act=false) =>
    `<li class="page-item ${dis?'disabled':''} ${act?'active':''}">
       <a class="page-link" ${dis?'':`onclick="${goFnName}(${p})"`}>${lbl}</a>
     </li>`;
  let html = '';
  html += mk(Math.max(1, page-1), '&laquo;', page===1, false);
  const win=2; let s=Math.max(1,page-win), e=Math.min(totalPages,page+win);
  for(let i=s;i<=e;i++) html += mk(i, i, false, i===page);
  html += mk(Math.min(totalPages, page+1), '&raquo;', page===totalPages, false);
  el.innerHTML = html;
  if (summaryEl){
    const from = (page-1)*pageSize + 1, to = Math.min(page*pageSize, totalItems);
    summaryEl.textContent = `${from}-${to} / ${totalItems} bản ghi`;
  }
}

// ---- Load PO page (server first, fallback client)
async function loadPOPage(){
  // thử gọi endpoint server-side
  const params = new URLSearchParams({ page: POState.page, pageSize: POState.pageSize });
  POState.statuses.forEach(s => params.append('status', s));
  try{
    const res = await fetch(`${API_POS(currentSupplierId)}?${params}`, { headers:{ ...authHeaders() }});
    if (res.ok){
      const data = await res.json();
      const items = data.items ?? data.Items ?? [];
      poBody.innerHTML = items.length ? items.map(p=>`
        <tr>
          <td>#${p.poid ?? p.POID}</td>
          <td>${fmt(p.createdAt ?? p.CreatedAt)}</td>
          <td><span class="badge badge-soft">${escapeHtml(p.status ?? p.Status)}</span></td>
          <td class="text-end">${p.lineCount ?? p.LineCount ?? 0}</td>
          <td class="text-end">${(p.totalQty ?? p.TotalQty ?? 0).toLocaleString('vi-VN')}</td>
        </tr>`).join('') : `<tr><td colspan="5" class="text-center text-muted py-3">Không có PO</td></tr>`;
      renderSubPager(poPager, data.totalItems ?? data.TotalItems ?? 0, data.page ?? data.Page ?? POState.page, data.pageSize ?? data.PageSize ?? POState.pageSize, 'gotoPO', poSummary);
      return;
    }
    // nếu 404 hoặc lỗi khác -> fallback
  }catch(e){ /* bỏ qua để fallback */ }

  // Fallback từ lastDetail (client-side paging)
  const all = (lastDetail?.purchaseOrders ?? lastDetail?.PurchaseOrders ?? []) || [];
  const filtered = POState.statuses.length ? all.filter(p => POState.statuses.includes(p.status ?? p.Status)) : all;
  const total = filtered.length;
  const start = (POState.page-1)*POState.pageSize, end = start + POState.pageSize;
  const pageItems = filtered.slice(start, end);
  poBody.innerHTML = pageItems.length ? pageItems.map(p=>`
    <tr>
      <td>#${p.poid ?? p.POID}</td>
      <td>${fmt(p.createdAt ?? p.CreatedAt)}</td>
      <td><span class="badge badge-soft">${escapeHtml(p.status ?? p.Status)}</span></td>
      <td class="text-end">${p.lineCount ?? p.LineCount ?? 0}</td>
      <td class="text-end">${(p.totalQty ?? p.TotalQty ?? 0).toLocaleString('vi-VN')}</td>
    </tr>`).join('') : `<tr><td colspan="5" class="text-center text-muted py-3">Không có PO</td></tr>`;
  renderSubPager(poPager, total, POState.page, POState.pageSize, 'gotoPO', poSummary);
}
window.gotoPO = function(p){ POState.page = p; loadPOPage(); };

// ---- Load Receipt page (server first, fallback client)
async function loadRCPage(){
  const params = new URLSearchParams({ page: RCState.page, pageSize: RCState.pageSize });
  RCState.statuses.forEach(s => params.append('status', s));
  try{
    const res = await fetch(`${API_RCS(currentSupplierId)}?${params}`, { headers:{ ...authHeaders() }});
    if (res.ok){
      const data = await res.json();
      const items = data.items ?? data.Items ?? [];
      rcBody.innerHTML = items.length ? items.map(r=>`
        <tr>
          <td>#${r.receiptID ?? r.ReceiptID}</td>
          <td>${fmt(r.createdAt ?? r.CreatedAt)}</td>
          <td><span class="badge badge-soft">${escapeHtml(r.status ?? r.Status)}</span></td>
          <td class="text-end">${r.detailCount ?? r.DetailCount ?? 0}</td>
          <td class="text-end">${(r.totalQty ?? r.TotalQty ?? 0).toLocaleString('vi-VN')}</td>
          <td class="text-end fw-semibold">${money(r.totalCost ?? r.TotalCost ?? 0)}</td>
        </tr>`).join('') : `<tr><td colspan="6" class="text-center text-muted py-3">Không có Receipt</td></tr>`;
      renderSubPager(rcPager, data.totalItems ?? data.TotalItems ?? 0, data.page ?? data.Page ?? RCState.page, data.pageSize ?? data.PageSize ?? RCState.pageSize, 'gotoRC', rcSummary);
      return;
    }
  }catch(e){ /* fallback */ }

  const all = (lastDetail?.receipts ?? lastDetail?.Receipts ?? []) || [];
  const filtered = RCState.statuses.length ? all.filter(r => RCState.statuses.includes(r.status ?? r.Status)) : all;
  const total = filtered.length;
  const start = (RCState.page-1)*RCState.pageSize, end = start + RCState.pageSize;
  const pageItems = filtered.slice(start, end);
  rcBody.innerHTML = pageItems.length ? pageItems.map(r=>`
    <tr>
      <td>#${r.receiptID ?? r.ReceiptID}</td>
      <td>${fmt(r.createdAt ?? r.CreatedAt)}</td>
      <td><span class="badge badge-soft">${escapeHtml(r.status ?? r.Status)}</span></td>
      <td class="text-end">${r.detailCount ?? r.DetailCount ?? 0}</td>
      <td class="text-end">${(r.totalQty ?? r.TotalQty ?? 0).toLocaleString('vi-VN')}</td>
      <td class="text-end fw-semibold">${money(r.totalCost ?? r.TotalCost ?? 0)}</td>
    </tr>`).join('') : `<tr><td colspan="6" class="text-center text-muted py-3">Không có Receipt</td></tr>`;
  renderSubPager(rcPager, total, RCState.page, RCState.pageSize, 'gotoRC', rcSummary);
}
window.gotoRC = function(p){ RCState.page = p; loadRCPage(); };

// ---- Filter apply buttons
btnReloadPO.addEventListener('click', ()=>{ POState.page = 1; POState.statuses = [...document.querySelectorAll('.po-filter:checked')].map(x=>x.value); loadPOPage(); });
btnReloadRC.addEventListener('click', ()=>{ RCState.page = 1; RCState.statuses = [...document.querySelectorAll('.rc-filter:checked')].map(x=>x.value); loadRCPage(); });

// ===== Events ở list chính =====
qEl.addEventListener('keydown', (e)=>{ if (e.key === 'Enter'){ State.q = qEl.value; State.page = 1; loadSuppliers(); } });
sortByEl.addEventListener('change', ()=>{ State.sortBy = sortByEl.value; State.page = 1; loadSuppliers(); });
sortDirEl.addEventListener('change', ()=>{ State.sortDir = sortDirEl.value; State.page = 1; loadSuppliers(); });
pageSizeEl.addEventListener('change', ()=>{ State.pageSize = +pageSizeEl.value; State.page = 1; loadSuppliers(); });
btnRefresh.addEventListener('click', ()=> loadSuppliers());

// ===== Init =====
document.addEventListener('DOMContentLoaded', ()=> {
  const sp = new URLSearchParams(location.search);
  if (sp.get('q')){ qEl.value = sp.get('q'); State.q = sp.get('q'); }
  if (sp.get('sortBy')){ sortByEl.value = sp.get('sortBy'); State.sortBy = sp.get('sortBy'); }
  if (sp.get('sortDir')){ sortDirEl.value = sp.get('sortDir'); State.sortDir = sp.get('sortDir'); }
  if (sp.get('pageSize')){ pageSizeEl.value = sp.get('pageSize'); State.pageSize = +sp.get('pageSize'); }
  else { State.pageSize = +pageSizeEl.value || 10; } // mặc định 10
  loadSuppliers();
});
