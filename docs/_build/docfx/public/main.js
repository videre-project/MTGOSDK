// Custom JavaScript for MTGOSDK documentation
// Injects version into navbar

(function() {
  'use strict';
  
  // Configuration injected during build
  var CONFIG = {
    versionMain: '1.0.6',
    versionSuffix: 'preview.2',
    commitHash: '',
    commitUrl: ''
  };

  console.log('MTGOSDK: Loading main.js', CONFIG);

  function init() {
    console.log('MTGOSDK: Initializing version injection');
    injectVersion();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
  
  function injectVersion() {
    var brand = document.querySelector('.navbar-brand');
    if (!brand) {
        console.warn('MTGOSDK: .navbar-brand not found');
        return;
    }
    
    // Find the text node containing "MTGOSDK"
    var textNodes = Array.from(brand.childNodes).filter(function(n) {
      return n.nodeType === 3 && n.textContent.trim();
    });
    if (textNodes.length === 0) {
        console.warn('MTGOSDK: Brand text node not found');
        return;
    }
    
    var textNode = textNodes[textNodes.length - 1];
    var baseName = textNode.textContent.trim();
    
    // Build version HTML
    var html = baseName + ' <span class="version-badge">v' + CONFIG.versionMain;
    if (CONFIG.versionSuffix) {
      html += '-' + CONFIG.versionSuffix;
    }
    if (CONFIG.commitHash) {
       html += '<span class="d-none d-sm-inline">'; // Hide hash on very small screens?
       if (CONFIG.commitUrl) {
           html += '+<a href="' + CONFIG.commitUrl + '" target="_blank" rel="noopener" class="commit-link">' + CONFIG.commitHash.substring(0,7) + '</a>';
       } else {
           html += '+' + CONFIG.commitHash.substring(0,7);
       }
       html += '</span>';
    }
    html += '</span>';
    
    var span = document.createElement('span');
    span.className = 'brand-with-version brand-text'; 
    span.innerHTML = html;
    textNode.parentNode.replaceChild(span, textNode);
    console.log('MTGOSDK: Version injected');
  }
})();
