(() => {
  const flashes = document.querySelectorAll('.flash');
  flashes.forEach((el) => {
    setTimeout(() => {
      el.style.transition = 'opacity .4s ease, transform .4s ease';
      el.style.opacity = '0';
      el.style.transform = 'translateY(-6px)';
    }, 4200);
  });

  const tiles = document.querySelectorAll('.facility-tile');
  tiles.forEach((tile, index) => {
    tile.style.animationDelay = `${index * 70}ms`;
  });
})();
