// ============================================================
// LMS Shared Utilities
// ============================================================

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

function showToast(msg, type = 'success', opts = {}) {
    const container = document.getElementById('toastContainer') || (() => {
        const c = document.createElement('div');
        c.id = 'toastContainer';
        c.className = 'toast-container';
        document.body.appendChild(c);
        return c;
    })();

    const icons = { success: '✓', error: '✕', warning: '!', info: 'i' };
    const titles = {
        success: 'Thành công',
        error: 'Lỗi hệ thống',
        warning: 'Lưu ý',
        info: 'Thông báo'
    };

    const toast = document.createElement('div');
    toast.className = `toast ${type} ${opts.persistent ? 'persistent' : ''}`;
    toast.innerHTML = `
        <span class="toast-icon">${icons[type] || 'i'}</span>
        <div class="toast-content">
            <div class="toast-title">${escapeHtml(opts.title || titles[type] || 'Thong bao')}</div>
            <div class="toast-message">${escapeHtml(msg)}</div>
        </div>
        <button type="button" class="toast-close" aria-label="Đóng">×</button>`;

    container.appendChild(toast);
    toast.querySelector('.toast-close')?.addEventListener('click', () => toast.remove());

    if (!opts.persistent) {
        setTimeout(() => toast.remove(), opts.duration || 4200);
    }

    return toast;
}

function fmtDate(dateStr) {
    if (!dateStr) return '--';
    const d = new Date(dateStr);
    return d.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

function fmtDateTime(dateStr) {
    if (!dateStr) return '--';
    const d = new Date(dateStr);
    return d.toLocaleString('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function fmtCurrency(num) {
    if (num === null || num === undefined) return '0 VND';
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(num);
}

function fmtNumber(num) {
    if (num === null || num === undefined) return '0';
    return new Intl.NumberFormat('vi-VN').format(num);
}

function debounce(fn, wait = 300) {
    let t;
    return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), wait); };
}

async function apiFetch(url, options = {}, timeoutMs = 15000) {
    const isFormData = options.body instanceof FormData || options.isFormData;
    let actualTimeout = timeoutMs;
    if (isFormData && timeoutMs === 15000) {
        actualTimeout = 1800000; // 30 minutes for large file uploads
    }
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), actualTimeout);
    try {
        const headers = { ...(options.headers || {}) };
        if (!isFormData && !headers['Content-Type']) {
            headers['Content-Type'] = 'application/json';
        }

        const res = await fetch(url, {
            headers,
            signal: controller.signal,
            ...options
        });
        clearTimeout(timer);

        const isJson = res.headers.get('content-type')?.includes('application/json');
        const redirectedToLogin = res.redirected && res.url && /\/Auth\/Login/i.test(res.url);

        if (redirectedToLogin) {
            throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
        }

        if (!res.ok) {
            let errorMsg = `Lỗi hệ thống (${res.status})`;
            if (isJson) {
                const errData = await res.json();
                errorMsg = errData.error || errData.title || errorMsg;
            } else {
                const text = await res.text();
                if (text && text.length < 300) errorMsg = text;
            }
            throw new Error(errorMsg);
        }

        if (isJson) {
            const data = await res.json();
            // Chỉ throw khi có error và không có data khác, tránh phá vỡ flow
            if (data && typeof data === 'object' && data.error && Object.keys(data).length === 1) {
                throw new Error(data.error);
            }
            return data;
        }

        const text = await res.text();
        if (typeof text === 'string' && /<form[^>]*action=\"\/Auth\/Login|<title>\s*Login/i.test(text)) {
            throw new Error('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
        }
        return text;
    } catch (err) {
        clearTimeout(timer);
        if (err.name === 'AbortError') {
            const timeoutErr = new Error('Request timeout - Server phản hồi quá chậm');
            showToast(timeoutErr.message, 'error');
            throw timeoutErr;
        }
        console.error('API Error:', url, err.message);
        showToast(err.message || 'Lỗi kết nối máy chủ', 'error');
        throw err;
    }
}

function statusBadge(status) {
    const map = {
        Active: '<span class="badge badge-success">Hoạt động</span>',
        Inactive: '<span class="badge badge-danger">Vô hiệu</span>',
        Completed: '<span class="badge badge-success">Hoàn thành</span>',
        InProgress: '<span class="badge badge-blue">Đang học</span>',
        NotStarted: '<span class="badge badge-gray">Chưa bắt đầu</span>',
        Published: '<span class="badge badge-success">Đã phát hành</span>',
        Draft: '<span class="badge badge-warning">Nháp</span>',
        High: '<span class="badge badge-danger">Cao</span>',
        Normal: '<span class="badge badge-blue">Bình thường</span>',
        Low: '<span class="badge badge-gray">Thấp</span>'
    };
    return map[status] || `<span class="badge badge-gray">${escapeHtml(status || 'N/A')}</span>`;
}

function progressBar(pct, color = '') {
    const c = pct >= 100 ? 'green' : pct >= 50 ? '' : 'orange';
    return `
    <div class="progress-wrap">
      <div class="progress-bar-track">
        <div class="progress-bar-fill ${color || c}" style="width:${Math.min(pct, 100)}%"></div>
      </div>
      <span class="progress-text">${pct}%</span>
    </div>`;
}

function getInitials(name) {
    if (!name) return '?';
    const parts = name.trim().split(' ');
    if (parts.length === 1) return parts[0][0].toUpperCase();
    return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
}

function setActivePage(id) {
    document.querySelectorAll('.nav-link').forEach(a => a.classList.remove('active'));
    const el = document.querySelector(`[data-page="${id}"]`);
    if (el) el.classList.add('active');
    const altEl = document.querySelector(`[data-nav-page="${id}"]`);
    if (altEl) altEl.classList.add('active');

    document.querySelectorAll('.page-section').forEach(s => {
        s.style.display = s.id === id ? '' : 'none';
    });

    // Tự động cuộn lên đầu trang khi chuyển mục
    const scrollContainer = document.querySelector('.main-content') || document.querySelector('.student-main');
    if (scrollContainer) {
        scrollContainer.scrollTop = 0;
    }
}

function openModal(id) {
    const m = document.getElementById(id);
    if (!m) return;
    const openCount = document.querySelectorAll('.modal-backdrop.open').length;
    m.style.zIndex = String(1000 + (openCount + 1) * 20);
    m.classList.add('open');
}

function closeModal(id) {
    const m = document.getElementById(id);
    if (m) m.classList.remove('open');
}

document.addEventListener('click', e => {
    if (e.target.classList.contains('modal-backdrop')) {
        e.target.classList.remove('open');
    }
});

function initializeTheme() {
    const savedTheme = localStorage.getItem('lms_theme') || 'light';
    document.documentElement.setAttribute('data-theme', savedTheme);
    updateThemeButton(savedTheme);
}

function updateThemeButton(theme) {
    document.querySelectorAll('#themeToggleBtn').forEach(btn => {
        btn.innerHTML = theme === 'dark'
            ? '<span class="nav-icon">☾</span><span class="nav-text">Dark mode</span>'
            : '<span class="nav-icon">☀</span><span class="nav-text">Light mode</span>';
    });
}

function toggleTheme() {
    const htmlEl = document.documentElement;
    const currentTheme = htmlEl.getAttribute('data-theme') || 'light';
    const nextTheme = currentTheme === 'dark' ? 'light' : 'dark';
    htmlEl.setAttribute('data-theme', nextTheme);
    localStorage.setItem('lms_theme', nextTheme);
    updateThemeButton(nextTheme);
}

function setSidebarOpen(isOpen) {
    document.body.classList.toggle('sidebar-open', !!isOpen);
    document.getElementById('sidebarBackdrop')?.classList.toggle('is-open', !!isOpen);
}

function initializeShell() {
    updateThemeButton(document.documentElement.getAttribute('data-theme') || 'light');

    document.querySelectorAll('[data-page]').forEach(link => {
        if (link.getAttribute('href') === '#') {
            link.addEventListener('click', event => {
                event.preventDefault();
                const page = link.getAttribute('data-page');
                if (page) {
                    setActivePage(page);
                    setSidebarOpen(false);
                }
            });
        }
    });

    document.querySelectorAll('.sidebar .nav-link').forEach(link => {
        link.addEventListener('click', () => {
            if (window.innerWidth <= 1024) {
                setSidebarOpen(false);
            }
        });
    });

    window.addEventListener('resize', () => {
        if (window.innerWidth > 1024) {
            setSidebarOpen(false);
        }
    });
}


