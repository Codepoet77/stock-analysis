/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        app: '#020817',
        'card-dark': '#0a0f1e',
      },
    },
  },
  plugins: [],
}
