const menuBtn = document.getElementById("menuToggle");
const closeBtn = document.getElementById("closeMenu"); // ✅ الجديد
const sideMenu = document.querySelector(".side-menu");
const overlay = document.querySelector(".menu-overlay");

/* فتح / إغلاق من زر القائمة */
menuBtn.addEventListener("click", () => {
    sideMenu.classList.toggle("open");
    overlay.classList.toggle("show");
});

/* إغلاق من زر X */
closeBtn.addEventListener("click", () => {
    sideMenu.classList.remove("open");
    overlay.classList.remove("show");
});

/* إغلاق عند الضغط على الخلفية */
overlay.addEventListener("click", () => {
    sideMenu.classList.remove("open");
    overlay.classList.remove("show");
});
