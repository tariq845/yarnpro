// ============================================
// SaaS Dashboard JavaScript
// ============================================

document.addEventListener('DOMContentLoaded', function () {
    initSidebar();
    initNavigation();
    initDropdowns();
    initHtmxEvents();
    initFormSubmitLoaders();
    initLinkLoaders();
    processToastData(); // Initial check on page load
});

// Process toasts from hidden DOM data
function processToastData() {
    const data = document.getElementById('toast-data');
    if (!data) return;

    if (data.dataset.success) showToast(data.dataset.success, 'success');
    if (data.dataset.error) showToast(data.dataset.error, 'error');
    if (data.dataset.warning) showToast(data.dataset.warning, 'warning');
    if (data.dataset.info) showToast(data.dataset.info, 'info');

    // Clear data attributes so they don't repeat on subsequent swaps
    data.dataset.success = "";
    data.dataset.error = "";
    data.dataset.warning = "";
    data.dataset.info = "";
}

// ============================================
// Sidebar Functions
// ============================================

function initSidebar() {
    const toggleBtn = document.getElementById('sidebarToggle');
    const sidebar = document.querySelector('.sidebar');
    const overlay = document.querySelector('.sidebar-overlay');
    const body = document.body;

    // Function to check if we're on mobile
    function isMobile() {
        return window.innerWidth <= 768;
    }

    // Function to handle responsive changes
    function handleResponsive() {
        if (isMobile()) {
            // On mobile, remove collapsed class and ensure sidebar is closed by default
            body.classList.remove('sidebar-collapsed');
            sidebar.classList.remove('mobile-open');
            if (overlay) {
                overlay.classList.remove('show');
            }
        } else {
            // On desktop, restore saved collapsed state
            if (localStorage.getItem('sidebarCollapsed') === 'true') {
                body.classList.add('sidebar-collapsed');
            }
        }
    }

    // Initial setup
    handleResponsive();

    // Single Toggle Button (works for both desktop and mobile)
    if (toggleBtn) {
        toggleBtn.addEventListener('click', function () {
            if (isMobile()) {
                // Mobile: Toggle sidebar visibility
                sidebar.classList.toggle('mobile-open');
                // Toggle overlay
                if (overlay) {
                    overlay.classList.toggle('show');
                }
            } else {
                // Desktop: Toggle sidebar collapse
                body.classList.toggle('sidebar-collapsed');
                localStorage.setItem('sidebarCollapsed', body.classList.contains('sidebar-collapsed'));
            }
        });
    }

    // Handle window resize
    window.addEventListener('resize', handleResponsive);

    // Close sidebar on overlay click (mobile)
    if (overlay) {
        overlay.addEventListener('click', function () {
            sidebar.classList.remove('mobile-open');
            overlay.classList.remove('show');
        });
    }

    // Submenu Toggle (Accordion Behavior)
    document.querySelectorAll('.nav-item.has-submenu > .nav-link').forEach(link => {
        link.addEventListener('click', function (e) {
            e.preventDefault();
            const parent = this.parentElement;
            const isOpen = parent.classList.contains('open');

            // Close all other submenus at the same level
            const siblings = parent.parentElement.querySelectorAll(':scope > .nav-item.has-submenu');
            siblings.forEach(sibling => {
                if (sibling !== parent) {
                    sibling.classList.remove('open');
                }
            });

            // Toggle the clicked one
            if (isOpen) {
                parent.classList.remove('open');
            } else {
                parent.classList.add('open');
            }
        });
    });
}

// ============================================
// Navigation Active State
// ============================================

function initNavigation() {
    const navLinks = document.querySelectorAll('.sidebar-nav .nav-link[href]:not(.has-submenu > .nav-link)');

    // Function to update active state based on current URL
    function updateActiveState() {
        const currentPath = window.location.pathname;
        const allLinks = document.querySelectorAll('.sidebar-nav .nav-link');

        // 1. Clear all active and open states first for a clean slate
        allLinks.forEach(link => link.classList.remove('active'));

        // 2. Clear all open states from parent items (accordion behavior)
        // Except we'll re-add 'open' for the current path
        document.querySelectorAll('.nav-item.has-submenu').forEach(item => {
            item.classList.remove('open');
        });

        navLinks.forEach(link => {
            const href = link.getAttribute('href');
            if (!href || href === '#' || href === 'javascript:void(0)') {
                return;
            }

            try {
                const linkUrl = new URL(link.href);
                const linkPath = linkUrl.pathname;

                // Precise matching: exact match
                if (currentPath === linkPath) {
                    link.classList.add('active');

                    // Highlight parents if it's a submenu item
                    let parentItem = link.closest('.nav-item.has-submenu');
                    while (parentItem) {
                        parentItem.classList.add('open');

                        // Look for higher level parents if nested
                        parentItem = parentItem.parentElement.closest('.nav-item.has-submenu');
                    }
                }
            } catch (e) {
                // Fallback for relative URLs
                if (currentPath === href) {
                    link.classList.add('active');
                    let parentItem = link.closest('.nav-item.has-submenu');
                    if (parentItem) {
                        parentItem.classList.add('open');
                    }
                }
            }
        });
    }

    // Add click event to all nav links (for mobile sidebar closing)
    navLinks.forEach(link => {
        link.addEventListener('click', function (e) {
            // If it's a submenu parent link, the initSidebar click handler handles it
            if (this.parentElement.classList.contains('has-submenu')) {
                return;
            }

            // Close mobile sidebar after clicking a leaf link
            if (window.innerWidth <= 768) {
                const sidebar = document.querySelector('.sidebar');
                const overlay = document.querySelector('.sidebar-overlay');
                sidebar.classList.remove('mobile-open');
                if (overlay) {
                    overlay.classList.remove('show');
                }
            }
        });
    });

    // Update active state after HTMX navigation
    document.addEventListener('htmx:afterSettle', function (evt) {
        updateActiveState();
    });

    // Initial active state update
    updateActiveState();
}

// ============================================
// Dropdown Functions
// ============================================

// ============================================
// Dropdown Functions
// ============================================

function initDropdowns() {
    // Dropdowns are now handled by Bootstrap 5 native functionality
    // This empty function is kept to prevent reference errors if called elsewhere
}

// ============================================
// YouTube-Style Top Progress Bar Engine
// ============================================

const TopProgress = {
    elem: null,
    progress: 0,
    timer: null,

    init: function () {
        if (!this.elem) {
            this.elem = document.getElementById('yis-top-loader');
            if (!this.elem) {
                this.elem = document.createElement('div');
                this.elem.id = 'yis-top-loader';
                document.body.appendChild(this.elem);
            }
        }
        return this.elem;
    },

    start: function () {
        const el = this.init();
        if (this.timer) {
            clearInterval(this.timer);
            this.timer = null;
        }

        el.style.transition = 'none';
        el.style.width = '0%';
        el.style.opacity = '1';
        this.progress = 0;

        // Force reflow
        void el.offsetWidth;

        el.style.transition = 'width 0.3s cubic-bezier(0.1, 0.9, 0.2, 1), opacity 0.2s ease';
        this.set(25 + Math.random() * 15);

        // Incremental YouTube style creep
        this.timer = setInterval(() => {
            if (this.progress < 70) {
                this.set(this.progress + Math.random() * 8 + 3);
            } else if (this.progress < 90) {
                this.set(this.progress + Math.random() * 2 + 0.5);
            }
        }, 200);
    },

    set: function (val) {
        this.progress = Math.min(val, 95);
        if (this.elem) {
            this.elem.style.width = this.progress + '%';
        }
    },

    done: function () {
        if (this.timer) {
            clearInterval(this.timer);
            this.timer = null;
        }
        if (!this.elem) return;

        this.elem.style.transition = 'width 0.2s ease-out, opacity 0.3s ease 0.2s';
        this.elem.style.width = '100%';
        this.elem.style.opacity = '0';

        setTimeout(() => {
            if (this.elem && this.elem.style.opacity === '0') {
                this.elem.style.width = '0%';
            }
        }, 500);
    }
};

function initTopLoader() {
    TopProgress.init();
}

function showTopLoader() {
    TopProgress.start();
}

function hideTopLoader() {
    TopProgress.done();
}

// ============================================
// Smart Inside-Button Loader System
// ============================================

function getButtonLoadingText(btn, form) {
    if (!btn) return '<span class="btn-loader-spinner"></span> Please wait...';

    if (btn.dataset.loadingText) {
        return `<span class="btn-loader-spinner"></span> ${btn.dataset.loadingText}`;
    }

    const rawText = (btn.innerText || btn.textContent || '').trim();
    const lower = rawText.toLowerCase();

    const isDelete = btn.querySelector('.bi-trash') ||
        btn.classList.contains('text-danger') ||
        btn.getAttribute('title')?.toLowerCase()?.includes('delete') ||
        form?.getAttribute('asp-page-handler')?.toLowerCase()?.includes('delete') ||
        form?.getAttribute('action')?.toLowerCase()?.includes('delete') ||
        lower.includes('delete') ||
        lower.includes('remove');

    if (isDelete) {
        if (!rawText) {
            return '<span class="btn-loader-spinner no-margin text-danger"></span>';
        }
        return '<span class="btn-loader-spinner text-danger"></span> Deleting...';
    }

    const path = window.location.pathname.toLowerCase();
    if (lower.includes('sign in') || lower.includes('log in') || lower.includes('login') || path.includes('/login')) {
        return '<span class="btn-loader-spinner"></span> Signing in...';
    }

    if (lower.includes('continue') || path.includes('/selectcompany')) {
        return '<span class="btn-loader-spinner"></span> Continuing...';
    }

    if (lower.includes('change password') || lower.includes('update password')) {
        return '<span class="btn-loader-spinner"></span> Updating Password...';
    }

    if (lower.includes('save')) {
        return '<span class="btn-loader-spinner"></span> Saving...';
    }

    if (lower.includes('update') || lower.includes('edit')) {
        return '<span class="btn-loader-spinner"></span> Updating...';
    }

    if (lower.includes('create')) {
        return '<span class="btn-loader-spinner"></span> Creating...';
    }

    if (lower.includes('add')) {
        return `<span class="btn-loader-spinner"></span> ${rawText || 'Adding'}...`;
    }

    if (lower.includes('search') || lower.includes('filter') || lower.includes('apply')) {
        return '<span class="btn-loader-spinner"></span> Filtering...';
    }

    if (lower.includes('post') || lower.includes('submit')) {
        return '<span class="btn-loader-spinner"></span> Submitting...';
    }

    if (!rawText) {
        return '<span class="btn-loader-spinner no-margin"></span>';
    }

    return `<span class="btn-loader-spinner"></span> ${rawText}...`;
}

function applyButtonLoader(btn, form) {
    if (!btn || btn.dataset.loadingActive === 'true') return;

    btn.dataset.originalHtml = btn.innerHTML;
    btn.dataset.loadingActive = 'true';

    // Lock width so button does not jump / resize
    const rect = btn.getBoundingClientRect();
    if (rect.width > 0) {
        btn.style.minWidth = `${rect.width}px`;
    }

    btn.innerHTML = getButtonLoadingText(btn, form);
    btn.disabled = true;
    btn.classList.add('disabled');
    btn.style.pointerEvents = 'none';
}

function restoreButtonLoader(btn) {
    if (!btn || btn.dataset.loadingActive !== 'true') return;

    if (btn.dataset.originalHtml) {
        btn.innerHTML = btn.dataset.originalHtml;
    }
    btn.dataset.loadingActive = 'false';
    btn.disabled = false;
    btn.classList.remove('disabled');
    btn.style.pointerEvents = '';
    btn.style.minWidth = '';
}

// ============================================
// HTMX Event Handlers
// ============================================

function initHtmxEvents() {
    // Before request - show top loader and button loader
    document.addEventListener('htmx:beforeRequest', function (evt) {
        showTopLoader();

        const elt = evt.detail.elt;
        if (elt) {
            if (elt.tagName === 'INPUT') {
                elt.classList.add('search-input-loading');
            }
            if (elt.tagName === 'BUTTON' || (elt.tagName === 'INPUT' && elt.type === 'submit') || elt.classList.contains('btn')) {
                applyButtonLoader(elt, elt.closest('form'));
            } else if (elt.tagName === 'FORM') {
                const btn = elt.querySelector('button[type="submit"], input[type="submit"], button:not([type]), .btn');
                if (btn) applyButtonLoader(btn, elt);
            }
        }
    });

    // After request - hide loading & restore buttons
    document.addEventListener('htmx:afterRequest', function (evt) {
        hideTopLoader();

        const elt = evt.detail.elt;
        if (elt) {
            if (elt.tagName === 'INPUT') {
                elt.classList.remove('search-input-loading');
            }
            if (elt.tagName === 'BUTTON' || (elt.tagName === 'INPUT' && elt.type === 'submit') || elt.classList.contains('btn')) {
                restoreButtonLoader(elt);
            } else if (elt.tagName === 'FORM') {
                const btn = elt.querySelector('button[type="submit"], input[type="submit"], button:not([type]), .btn');
                if (btn) restoreButtonLoader(btn);
            }
        }
    });

    // Handle errors
    document.addEventListener('htmx:responseError', function (evt) {
        hideTopLoader();
        console.error('HTMX Error:', evt.detail);
        showToast('An error occurred. Please try again.', 'error');
    });

    // After swap - reinitialize components
    document.addEventListener('htmx:afterSwap', function (evt) {
        initDropdowns();
        processToastData();
    });

    // Handle showToast event triggered by HX-Trigger header
    document.addEventListener('showToast', function (evt) {
        if (evt.detail) {
            showToast(evt.detail.message || evt.detail.value, evt.detail.type || 'info');
        }
    });
}

// ============================================
// Page Navigation Link Loaders (Index, Pagination, Buttons)
// ============================================

function initLinkLoaders() {
    document.addEventListener('click', function (e) {
        const link = e.target.closest('a');
        if (!link) return;

        const href = link.getAttribute('href');
        if (!href || href === '#' || href.startsWith('javascript:') || href.startsWith('data:') || link.getAttribute('target') === '_blank') {
            return;
        }

        // Don't trigger for HTMX links (HTMX handles them)
        if (link.hasAttribute('hx-get') || link.hasAttribute('hx-post') || link.hasAttribute('hx-delete') || link.hasAttribute('hx-put')) {
            return;
        }

        // If it's a button link (e.g. class="btn btn-primary", "btn btn-outline-*", etc.)
        if (link.classList.contains('btn') || link.classList.contains('btn-primary')) {
            applyButtonLoader(link);
        }

        // Trigger top glowing loader on navigation
        showTopLoader();
    });
}

// ============================================
// Toast Notifications
// ============================================

function showToast(message, type = 'info') {
    const toastContainer = document.getElementById('toast-container');
    if (!toastContainer) {
        console.error("Toast container not found!");
        return;
    }

    const toast = document.createElement('div');
    toast.className = `yis-toast yis-toast-${type}`;
    toast.style.pointerEvents = 'auto'; // Re-enable clicks for the toast itself
    toast.innerHTML = `
        <div class="yis-toast-content">
            <i class="bi bi-${getToastIcon(type)}"></i>
            <span>${message}</span>
        </div>
        <button class="yis-toast-close" onclick="this.parentElement.remove()">
            <i class="bi bi-x"></i>
        </button>
    `;

    toastContainer.appendChild(toast);

    // Auto remove after 5 seconds
    setTimeout(() => {
        if (toast.parentElement) {
            toast.classList.add('yis-toast-fade-out');
            setTimeout(() => toast.remove(), 300);
        }
    }, 5000);
}

function getToastIcon(type) {
    const icons = {
        success: 'check-circle-fill',
        error: 'exclamation-circle-fill',
        warning: 'exclamation-triangle-fill',
        info: 'info-circle-fill'
    };
    return icons[type] || icons.info;
}

// ============================================
// Utility Functions
// ============================================

function formatCurrency(amount) {
    return new Intl.NumberFormat('en-IN', {
        style: 'currency',
        currency: 'INR'
    }).format(amount);
}

function formatDate(dateString) {
    return new Date(dateString).toLocaleDateString('en-IN', {
        year: 'numeric',
        month: 'short',
        day: 'numeric'
    });
}

// Confirm Dialog
function confirmAction(message, callback) {
    if (confirm(message)) {
        callback();
    }
}

// ============================================
// Form Submit & Button Loader (PostBacks)
// ============================================

function initFormSubmitLoaders() {
    initTopLoader();

    // Track clicked button
    let lastClickedSubmitBtn = null;
    document.addEventListener('click', function (e) {
        const btn = e.target.closest('button[type="submit"], input[type="submit"], button:not([type]), .btn-submit-loader, .btn, .btn-primary');
        if (btn) {
            lastClickedSubmitBtn = btn;
        }
    }, true);

    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!form || form.tagName !== 'FORM') return;

        // Skip HTMX forms (handled in initHtmxEvents)
        if (form.getAttribute('hx-post') || form.getAttribute('hx-get') || form.getAttribute('hx-put') || form.getAttribute('hx-delete')) {
            return;
        }

        // 1. Check jQuery Unobtrusive Validation if active
        if (window.jQuery) {
            const $form = window.jQuery(form);
            if ($form.data('validator') && !$form.valid()) {
                return; // Form is invalid, do not show loader
            }
        }

        // 2. Check HTML5 validity if applicable
        if (form.checkValidity && !form.noValidate && !form.checkValidity()) {
            return;
        }

        // 3. Prevent double submit
        if (form.dataset.submitting === 'true') {
            e.preventDefault();
            return false;
        }
        form.dataset.submitting = 'true';

        // 4. Find submit button
        const submitBtn = lastClickedSubmitBtn && form.contains(lastClickedSubmitBtn)
            ? lastClickedSubmitBtn
            : form.querySelector('button[type="submit"], input[type="submit"], button:not([type])');

        if (submitBtn) {
            applyButtonLoader(submitBtn, form);
        }

        // 5. Activate YouTube Top Progress Loader
        showTopLoader();
    });

    // Reset when navigating back or cache restore
    window.addEventListener('pageshow', function () {
        hideTopLoader();

        document.querySelectorAll('form').forEach(f => {
            f.dataset.submitting = 'false';
            f.querySelectorAll('button, input[type="submit"]').forEach(btn => {
                restoreButtonLoader(btn);
            });
        });
    });
}
