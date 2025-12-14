
import './style.css';

// Theme Switching Logic
const themeBtn = document.getElementById('theme-toggle');
const logoImg = document.getElementById('app-logo');
const html = document.documentElement;

// Function to set theme
const setTheme = (theme) => {
  html.setAttribute('data-theme', theme);
  localStorage.setItem('theme', theme);

  // Swap Logo
  const heroLogo = document.getElementById('hero-logo');
  if (theme === 'light') {
    // Light Mode -> Needs Dark Logo
    logoImg.src = '/LogoDark256.png';
    if (heroLogo) heroLogo.src = '/LogoDark256.png';
  } else {
    // Dark Mode -> Needs Light Logo
    logoImg.src = '/LogoLight256.png';
    if (heroLogo) heroLogo.src = '/LogoLight256.png';
  }
};

// Check for saved preference or system preference
const savedTheme = localStorage.getItem('theme');
const systemPrefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;

if (savedTheme) {
  setTheme(savedTheme);
} else {
  setTheme(systemPrefersDark ? 'dark' : 'light');
}

// Watch for system theme changes if no preference is saved
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
  if (!localStorage.getItem('theme')) {
    setTheme(e.matches ? 'dark' : 'light');
  }
});

// Toggle Button Click
themeBtn.addEventListener('click', () => {
  const currentTheme = html.getAttribute('data-theme');
  const newTheme = currentTheme === 'light' ? 'dark' : 'light';
  setTheme(newTheme);
});


// Smooth scrolling for anchor links
document.querySelectorAll('a[href^="#"]').forEach(anchor => {
  anchor.addEventListener('click', function (e) {
    e.preventDefault();
    document.querySelector(this.getAttribute('href')).scrollIntoView({
      behavior: 'smooth'
    });
  });
});

// Intersection Observer for fade-in animations
const observerOptions = {
  threshold: 0.1
};

const observer = new IntersectionObserver((entries) => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
    }
  });
}, observerOptions);

document.querySelectorAll('.feature-card, .download-card').forEach(el => {
  el.style.opacity = '0';
  el.style.transform = 'translateY(20px)';
  el.style.transition = 'opacity 0.6s ease-out, transform 0.6s ease-out';
  observer.observe(el);
});

// Add visible class styling dynamically
const style = document.createElement('style');
style.textContent = `
  .visible {
    opacity: 1 !important;
    transform: translateY(0) !important;
  }
`;
document.head.appendChild(style);
