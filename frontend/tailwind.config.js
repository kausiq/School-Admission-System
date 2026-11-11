/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      fontFamily: { sans: ['"Segoe UI"', '"Noto Sans Bengali"', 'system-ui', 'sans-serif'] },
      colors: { brand: { 50: '#f3f1ff', 100: '#e7e4ff', 500: '#6c5ce7', 600: '#5b4bd5', 700: '#4a3cb8' } },
    },
  },
  plugins: [],
};
