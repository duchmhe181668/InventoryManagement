// ========= Helpers cơ bản =========
const $ = (s, r = document) => r.querySelector(s);
const fmt = v => (v ?? 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 });

function getApiBase() {
  return localStorage.getItem('apiBase') || 'https://localhost:7225';
}
function setApiBase(v) {
  localStorage.setItem('apiBase', v);
  const ip = $('#api-base');
  if (ip) ip.value = v;
}

// ========= Auth helper (lấy storeId giống các màn Store khác) =========
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

  // nếu chưa có storeId thì đọc từ JWT
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

function getStoreIdFromAuth() {
  const v = getAuthPayload()?.storeId;
  return typeof v === 'number' && !Number.isNaN(v) ? v : null;
}

// fetch JSON kèm Authorization
async function fetchJSON(url, options = {}) {
  const auth = getAuthPayload();
  const headers = { ...(options.headers || {}) };
  if (auth?.accessToken) {
    headers['Authorization'] = `${auth.tokenType || 'Bearer'} ${auth.accessToken}`;
  }
  if (options.body && !(options.body instanceof FormData) && !headers['Content-Type']) {
    headers['Content-Type'] = 'application/json';
  }

  const res = await fetch(url, { ...options, headers });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(text || res.statusText);
  }
  if (res.status === 204) return null;
  return res.json();
}

// helper pick thuộc tính (vì backend đôi khi Name/ name / GoodName)
function pick(obj, ...keys) {
  for (const k of keys) {
    if (obj && obj[k] != null && obj[k] !== '') return obj[k];
  }
  return null;
}

// ========= Lấy query & state =========
const query = new URLSearchParams(window.location.search);
const goodId = Number(query.get('goodId') || query.get('id') || 0);
let storeId = query.get('storeId') ? Number(query.get('storeId')) : null;
if (!storeId || Number.isNaN(storeId)) {
  storeId = getStoreIdFromAuth();
}

let currentSellPrice = null;

// ========= Load chi tiết =========
async function loadDetail() {
  const msg = $('#mainMsg');
  msg.textContent = '';

  if (!goodId) {
    msg.textContent = 'Thiếu goodId trên URL (vd: store-good-detail.html?goodId=1).';
    return;
  }
  if (!storeId) {
    msg.textContent = 'Không xác định được Store ID (từ URL hoặc token đăng nhập).';
    return;
  }

  const apiBase = getApiBase();
  const apiGoodsById       = `${apiBase}/api/Goods/${goodId}`;
  const apiPriceCurrent    = `${apiBase}/api/StorePrices/current?storeId=${storeId}&goodId=${goodId}`;

  msg.textContent = 'Đang tải dữ liệu...';

  try {
    // 1) Lấy thông tin hàng (Goods)
    const g = await fetchJSON(apiGoodsById);

    const name    = pick(g, 'name', 'Name');
    const sku     = pick(g, 'sku', 'SKU');
    const barcode = pick(g, 'barcode', 'Barcode');
    const unit    = pick(g, 'unit', 'Unit');
    const imgUrl  = pick(g, 'imageURL', 'imageUrl', 'ImageURL');
    const catName = pick(g, 'categoryName', 'CategoryName');
    const priceCost = Number(pick(g, 'priceCost', 'PriceCost') ?? 0);
    const defaultPriceSell = Number(pick(g, 'priceSell', 'PriceSell') ?? 0);

    if (imgUrl) {
      $('#goodImage').src = imgUrl;
      $('#goodImage').alt = name || 'image';
    }

    $('#goodName').textContent     = name || '—';
    $('#goodCategory').textContent = catName || '—';
    $('#goodSKU').textContent      = sku || '—';
    $('#goodBarcode').textContent  = barcode || '—';
    $('#goodUnit').textContent     = unit || '—';
    $('#goodCost').textContent     = fmt(priceCost);

    // 2) Lấy giá bán hiện tại theo StorePrices/current
    let storePriceSell = null;
    try {
      const priceData = await fetchJSON(apiPriceCurrent);
      if (priceData && (priceData.priceSell != null || priceData.PriceSell != null)) {
        storePriceSell = Number(priceData.priceSell ?? priceData.PriceSell);
      }
    } catch {
      // nếu không có record StorePrices, ta dùng giá trên Goods
    }

    currentSellPrice = storePriceSell ?? defaultPriceSell;
    $('#goodSell').textContent = fmt(currentSellPrice);

    // 3) Lấy tồn kho (OnHand / Reserved / InTransit / Available) theo StocksController
    try {
      const searchSku = encodeURIComponent(sku || '');
      const stockUrl = `${apiBase}/api/stocks/store/${storeId}?search=${searchSku}&page=1&pageSize=50&sort=sku_asc`;
      const stocksPaged = await fetchJSON(stockUrl);
      const items = stocksPaged.items || stocksPaged.Items || [];
      let it = items.find(x => {
        const gid = Number(pick(x, 'goodId', 'GoodId', 'goodID', 'GoodID'));
        return gid === goodId;
      }) || items[0];

      if (it) {
        const onHand   = Number(pick(it, 'onHand', 'OnHand') ?? 0);
        const reserved = Number(pick(it, 'reserved', 'Reserved') ?? 0);
        const inTran   = Number(pick(it, 'inTransit', 'InTransit') ?? 0);
        const avail    = Number(pick(it, 'available', 'Available') ?? (onHand - reserved));

        $('#statOnHand').textContent    = fmt(onHand);
        $('#statReserved').textContent  = fmt(reserved);
        $('#statInTransit').textContent = fmt(inTran);
        $('#statAvailable').textContent = fmt(avail);
        $('#goodAvailable').textContent = fmt(avail);
      }
    } catch {
      // nếu lỗi tồn kho thì giữ dấu gạch ngang
    }

    msg.textContent = '';
  } catch (err) {
    console.error(err);
    msg.textContent = 'Không tải được dữ liệu: ' + err.message;
  }
}

// ========= Sửa giá (dùng đúng API /api/StorePrices/current giống store-goods.js) =========
function setupPriceModal() {
  const btn = $('#btn-edit-price');
  const form = $('#priceForm');
  const msg = $('#priceMsg');
  const modal = new bootstrap.Modal('#priceModal');

  // nếu bạn muốn bỏ hẳn nút sửa giá, chỉ cần comment cả function này + phần gọi trong init
  if (!btn || !form) return;

  btn.addEventListener('click', () => {
    if (!storeId || !goodId) return;
    msg.textContent = '';
    form.price.value = currentSellPrice ?? 0;
    form.effectiveFrom.value = '';
    modal.show();
  });

  form.addEventListener('submit', async e => {
    e.preventDefault();
    msg.textContent = '';

    if (!storeId || !goodId) {
      msg.textContent = 'Thiếu StoreId hoặc GoodId.';
      return;
    }

    const newPrice = Number(form.price.value || 0);
    if (!Number.isFinite(newPrice) || newPrice <= 0) {
      msg.textContent = 'Giá bán phải > 0.';
      return;
    }
    const eff = form.effectiveFrom.value;

    const body = {
      storeId,
      goodId,
      priceSell: newPrice
    };
    if (eff) body.effectiveFrom = eff;

    msg.textContent = 'Đang lưu...';

    try {
      const res = await fetch(`${getApiBase()}/api/StorePrices/current`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
      });

      if (res.ok) {
        currentSellPrice = newPrice;
        msg.textContent = '';
        modal.hide();
        await loadDetail(); // reload lại để sync UI (available, v.v. giữ nguyên)
      } else {
        msg.textContent = 'Không lưu được giá.';
      }
    } catch (err) {
      console.error(err);
      msg.textContent = 'Lỗi kết nối: ' + err.message;
    }
  });
}

// ========= Init =========
document.addEventListener('DOMContentLoaded', () => {
  const apiInput = $('#api-base');
  if (apiInput) apiInput.value = getApiBase();
  const btnSetApi = $('#btn-set-api');
  if (btnSetApi) {
    btnSetApi.addEventListener('click', () => {
      const v = (apiInput.value || '').trim() || 'https://localhost:7225';
      setApiBase(v);
      loadDetail();
    });
  }

  setupPriceModal();
  loadDetail();
});
