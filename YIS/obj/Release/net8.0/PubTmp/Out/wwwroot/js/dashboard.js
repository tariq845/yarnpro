// ============================================
// SaaS Dashboard JavaScript
// ============================================

document.addEventListener('DOMContentLoaded', function () {
    initSidebar();
    initNavigation();
    initDropdowns();
    initHtmxEvents();
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
// HTMX Event Handlers
// ============================================

function initHtmxEvents() {
    // Before request - show loading
    document.addEventListener('htmx:beforeRequest', function (evt) {
        const target = evt.detail.target;
        if (target) {
            target.classList.add('htmx-loading');
        }
    });

    // After request - hide loading
    document.addEventListener('htmx:afterRequest', function (evt) {
        const target = evt.detail.target;
        if (target) {
            target.classList.remove('htmx-loading');
        }
    });

    // Handle errors
    document.addEventListener('htmx:responseError', function (evt) {
        console.error('HTMX Error:', evt.detail);
        showToast('An error occurred. Please try again.', 'error');
    });

    // After swap - reinitialize components
    document.addEventListener('htmx:afterSwap', function (evt) {
        // Reinitialize any components in the swapped content
        initDropdowns();
        processToastData(); // Check for any new toasts in the swapped content
    });

    // Handle showToast event triggered by HX-Trigger header
    document.addEventListener('showToast', function (evt) {
        if (evt.detail) {
            showToast(evt.detail.message || evt.detail.value, evt.detail.type || 'info');
        }
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
