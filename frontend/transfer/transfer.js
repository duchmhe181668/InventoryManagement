
  const API_BASE = 'https://localhost:7225/api/orders';
  const CREATED_BY = 1; // nếu backend lấy từ JWT thì bỏ

  let items = [];        // {goodId,name,barcode,unit,available,qty}
  let fromWarehouse = null; // {id,name}  — Nguồn
  let toStore = null;       // {id,name}  — Đích

  const debounce = (fn, ms=300) => { let t; return (...a)=>{ clearTimeout(t); t=setTimeout(()=>fn(...a), ms);} };
  const esc = s => (s??'').replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

  function showToast(message, variant='primary', delay=2600){
    const container = document.getElementById('toastContainer');
    const el = document.createElement('div');
    const color = ({success:'success', error:'danger', warning:'warning', info:'primary', primary:'primary'})[variant] || 'primary';
    el.className = `toast align-items-center bg-white border border-2 border-${color} text-${color}`;
    el.setAttribute('role','alert'); el.setAttribute('aria-live','assertive'); el.setAttribute('aria-atomic','true');
    el.innerHTML = `
      <div class="d-flex">
        <div class="toast-body fw-semibold">${esc(message)}</div>
        <button type="button" class="btn-close me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
      </div>`;
    container.appendChild(el);
    const t = new bootstrap.Toast(el, { delay });
    el.addEventListener('hidden.bs.toast', ()=> el.remove());
    t.show();
  }

  function renderItems(){
    const tb = document.getElementById('itemsBody');
    tb.innerHTML = '';
    items.forEach((it, idx)=>{
      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td>${idx+1}</td>
        <td>${esc(it.name)}</td>
        <td>${esc(it.barcode || '')}</td>
        <td>${esc(it.unit || '')}</td>
        <td>${(it.available ?? 0)}</td>
        <td>
          <input type="number" min="1" step="1" inputmode="numeric"
                 class="form-control form-control-sm qty-input"
                 value="${it.qty}" data-idx="${idx}">
        </td>
        <td class="text-center">
          <button class="btn btn-outline-danger icon-btn" data-del="${idx}" title="Xóa">
            <i class="fa-solid fa-trash-can"></i>
          </button>
        </td>`;
      tb.appendChild(tr);
    });
    document.getElementById('totalItems').textContent = items.length.toString();

    tb.querySelectorAll('input.qty-input').forEach(inp=>{
      inp.addEventListener('change',(e)=>{
        const i = Number(e.target.dataset.idx);
        let v = parseInt(e.target.value, 10);
        if(!Number.isFinite(v) || v < 1) v = 1;
        items[i].qty = v; e.target.value = v;
      });
    });
    tb.querySelectorAll('button[data-del]').forEach(btn=>{
      btn.addEventListener('click', e=>{
        const i = Number(e.currentTarget.dataset.del);
        items.splice(i,1);
        renderItems();
      });
    });
  }

  // Cập nhật tồn khả dụng theo Kho (nguồn) cho các dòng đã có
  async function refreshAvailability() {
    if (!fromWarehouse || items.length === 0) return;

    try {
      // Fallback an toàn: tra từng món dựa vào barcode/tên
      await Promise.all(items.map(async (it) => {
        const q = it.barcode || it.name;
        const url = `${API_BASE}/lookups/goods?q=${encodeURIComponent(q)}&top=1&locationId=${fromWarehouse.id}`;
        const r = await fetch(url, { headers: { 'Accept': 'application/json' } });
        if (!r.ok) return;
        const data = await r.json();
        const row = data.find(d => (d.goodID ?? d.GoodID) === it.goodId) || data[0];
        if (row && typeof row.available === 'number') it.available = row.available;
      }));
      renderItems();
    } catch (err) {
      console.error(err);
    }
  }

  // ===== Tìm hàng (available theo Kho nguồn) =====
  const goodSearch = document.getElementById('goodSearch');
  const goodBox = document.getElementById('goodResults');
  const goodUl = goodBox.querySelector('ul');

  goodSearch.addEventListener('input', debounce(async (e)=>{
    const q = e.target.value.trim();
    if(!q){ goodBox.classList.add('d-none'); goodUl.innerHTML=''; return; }
    try{
      const params = new URLSearchParams({ q, top: '20' });
      if(fromWarehouse) params.set('locationId', fromWarehouse.id); // lấy available theo Kho (nguồn)
      const res = await fetch(`${API_BASE}/lookups/goods?${params}`, { headers:{'Accept':'application/json'} });
      const data = await res.json(); // [{goodID,name,barcode,unit,available?}]
      goodUl.innerHTML = '';
      data.forEach(g=>{
        const li = document.createElement('li');
        li.className = 'list-group-item list-group-item-action';
        li.innerHTML = `
          <div class="d-flex justify-content-between align-items-center">
            <div>
              <div class="fw-semibold">${esc(g.name)}</div>
              <div class="text-muted small">Mã SP: ${esc(g.barcode||'')}${g.unit? ' • Đơn vị: '+esc(g.unit):''}</div>
            </div>
            ${typeof g.available === 'number' ? `<span class="badge bg-light text-dark">Khả dụng: ${g.available}</span>`:''}
          </div>`;
        li.addEventListener('click', ()=>{
          const idx = items.findIndex(x=>x.goodId===g.goodID);
          if(idx>=0){ items[idx].qty = parseInt(items[idx].qty,10)+1; }
          else {
            items.push({
              goodId: g.goodID,
              name: g.name,
              barcode: g.barcode,
              unit: g.unit || '',
              available: (typeof g.available==='number'? g.available : null),
              qty: 1
            });
          }
          renderItems();
          goodBox.classList.add('d-none'); goodUl.innerHTML=''; goodSearch.value='';
        });
        goodUl.appendChild(li);
      });
      goodBox.classList.toggle('d-none', data.length===0);
    }catch(err){
      console.error(err);
      goodBox.classList.add('d-none');
    }
  }, 300));

  document.getElementById('btnAddNewGood').addEventListener('click', ()=>{});

  // ===== Từ kho (nguồn) =====
  const fromWhSearch = document.getElementById('fromWhSearch');
  const fromWhBox = document.getElementById('fromWhResults');
  const fromWhUl = fromWhBox.querySelector('ul');

  fromWhSearch.addEventListener('input', debounce(async (e)=>{
    const q = e.target.value.trim().toLowerCase();
    try{
      const url = `${API_BASE}/lookups/locations?type=WAREHOUSE`;
      const res = await fetch(url, { headers:{'Accept':'application/json'} });
      const data = await res.json();
      const filtered = q? data.filter(x=> (x.name||'').toLowerCase().includes(q)) : data;
      fromWhUl.innerHTML = '';
      filtered.forEach(l=>{
        const li = document.createElement('li');
        li.className = 'list-group-item list-group-item-action';
        li.textContent = l.name;
        li.addEventListener('click', ()=>{
          fromWarehouse = { id: l.locationID, name: l.name };
          fromWhSearch.value = l.name;
          fromWhBox.classList.add('d-none'); fromWhUl.innerHTML='';
          // Xóa available cũ và nạp lại theo kho nguồn
          items = items.map(it => ({...it, available: null}));
          renderItems();
          refreshAvailability();
        });
        fromWhUl.appendChild(li);
      });
      fromWhBox.classList.toggle('d-none', filtered.length===0);
    }catch(err){
      console.error(err);
      fromWhBox.classList.add('d-none');
    }
  }, 200));

  // ===== Đến cửa hàng (đích) =====
  const toStoreSearch = document.getElementById('toStoreSearch');
  const toStoreBox = document.getElementById('toStoreResults');
  const toStoreUl = toStoreBox.querySelector('ul');

  toStoreSearch.addEventListener('input', debounce(async (e)=>{
    const q = e.target.value.trim().toLowerCase();
    try{
      const url = `${API_BASE}/lookups/locations?type=STORE`;
      const res = await fetch(url, { headers:{'Accept':'application/json'} });
      const data = await res.json();
      const filtered = q? data.filter(x=> (x.name||'').toLowerCase().includes(q)) : data;
      toStoreUl.innerHTML = '';
      filtered.forEach(l=>{
        const li = document.createElement('li');
        li.className = 'list-group-item list-group-item-action';
        li.textContent = l.name;
        li.addEventListener('click', ()=>{
          toStore = { id: l.locationID, name: l.name };
          toStoreSearch.value = l.name;
          toStoreBox.classList.add('d-none'); toStoreUl.innerHTML='';
        });
        toStoreUl.appendChild(li);
      });
      toStoreBox.classList.toggle('d-none', filtered.length===0);
    }catch(err){
      console.error(err);
      toStoreBox.classList.add('d-none');
    }
  }, 200));

  // ===== Xóa hết / Xác nhận =====
  document.getElementById('btnClearAll').addEventListener('click', ()=>{
    if(items.length===0) return;
    if(confirm('Xóa hết các mặt hàng trong danh sách?')){
      items = [];
      renderItems();
    }
  });

  document.getElementById('btnConfirm').addEventListener('click', async ()=>{
    if(!fromWarehouse){ showToast('Vui lòng chọn "Từ kho (nguồn)".', 'warning'); return; }
    if(!toStore){ showToast('Vui lòng chọn "Đến cửa hàng (đích)".', 'warning'); return; }
    if(items.length===0){ showToast('Danh sách mặt hàng đang trống.', 'warning'); return; }

    const payload = {
      fromLocationID: fromWarehouse.id,
      toLocationID: toStore.id,
      createdBy: CREATED_BY,
      items: items.map(it => ({ goodID: it.goodId, quantity: parseInt(it.qty,10) }))
    };
    try{
      const res = await fetch(`${API_BASE}/transfers`, {
        method:'POST',
        headers:{'Content-Type':'application/json','Accept':'application/json'},
        body: JSON.stringify(payload)
      });
      if(!res.ok) throw new Error(await res.text() || ('HTTP '+res.status));
      await res.json();
      showToast('Tạo phiếu điều chuyển thành công!', 'success');
      items = []; renderItems();
    }catch(err){
      console.error(err);
      showToast('Lỗi khi lưu phiếu điều chuyển', 'error');
    }
  });

  // Đóng dropdowns khi click ngoài
  document.addEventListener('click', (e)=>{
    if(!document.getElementById('goodResults').contains(e.target) && e.target !== goodSearch){
      goodBox.classList.add('d-none');
    }
    if(!document.getElementById('fromWhResults').contains(e.target) && e.target !== fromWhSearch){
      fromWhBox.classList.add('d-none');
    }
    if(!document.getElementById('toStoreResults').contains(e.target) && e.target !== toStoreSearch){
      toStoreBox.classList.add('d-none');
    }
  });
