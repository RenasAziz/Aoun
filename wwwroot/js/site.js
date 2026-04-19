document.addEventListener("DOMContentLoaded", function () {
    var menuBtn = document.getElementById("menuToggle");
    var closeBtn = document.getElementById("closeMenu");
    var sideMenu = document.querySelector(".side-menu");
    var overlay = document.querySelector(".menu-overlay");

    var notifToggle = document.getElementById("notifToggle");
    var notifDropdown = document.getElementById("notifDropdown");
    var markAllReadBtn = document.getElementById("markAllReadBtn");

    if (menuBtn && sideMenu && overlay) {
        menuBtn.addEventListener("click", function () {
            sideMenu.classList.toggle("open");
            overlay.classList.toggle("show");
        });
    }

    if (closeBtn && sideMenu && overlay) {
        closeBtn.addEventListener("click", function () {
            sideMenu.classList.remove("open");
            overlay.classList.remove("show");
        });
    }

    if (overlay && sideMenu) {
        overlay.addEventListener("click", function () {
            sideMenu.classList.remove("open");
            overlay.classList.remove("show");

            if (notifDropdown) {
                notifDropdown.classList.remove("show");
            }
        });
    }

    if (notifToggle && notifDropdown) {
        notifToggle.addEventListener("click", function (e) {
            e.stopPropagation();
            notifDropdown.classList.toggle("show");
        });

        notifDropdown.addEventListener("click", function (e) {
            e.stopPropagation();
        });

        document.addEventListener("click", function (e) {
            if (!notifDropdown.contains(e.target) && !notifToggle.contains(e.target)) {
                notifDropdown.classList.remove("show");
            }
        });
    }

    document.querySelectorAll(".notif-item").forEach(function (item) {
        item.addEventListener("click", function () {
            var id = this.getAttribute("data-id");
            var url = this.getAttribute("data-mark-read-url");

            if (!id || !url) return;

            if (navigator.sendBeacon) {
                var formData = new FormData();
                formData.append("id", id);
                navigator.sendBeacon(url, formData);
            } else {
                fetch(url, {
                    method: "POST",
                    headers: {
                        "Content-Type": "application/x-www-form-urlencoded"
                    },
                    body: "id=" + encodeURIComponent(id),
                    keepalive: true
                });
            }
        });
    });

    if (markAllReadBtn) {
        markAllReadBtn.addEventListener("click", function (e) {
            e.stopPropagation();

            var url = this.getAttribute("data-url");
            if (!url) return;

            fetch(url, {
                method: "POST"
            })
                .then(function (response) {
                    if (!response.ok) {
                        throw new Error("Request failed");
                    }
                    return response.json();
                })
                .then(function () {
                    var badge = document.getElementById("notifBadge");
                    if (badge) {
                        badge.remove();
                    }

                    var actionsBox = markAllReadBtn.closest(".notif-dropdown__actions");
                    if (actionsBox) {
                        actionsBox.remove();
                    }

                    var listBox = document.querySelector(".notif-dropdown__list");
                    if (listBox) {
                        listBox.remove();
                    }

                    var dropdown = document.getElementById("notifDropdown");
                    if (dropdown && !dropdown.querySelector(".notif-dropdown__empty")) {
                        var emptyBox = document.createElement("div");
                        emptyBox.className = "notif-dropdown__empty";
                        emptyBox.textContent = "لا توجد إشعارات جديدة";
                        dropdown.appendChild(emptyBox);
                    }
                })
                .catch(function (error) {
                    console.error(error);
                });
        });
    }
});