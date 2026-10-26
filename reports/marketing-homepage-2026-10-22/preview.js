const shots={completion:['assets/completion.png','SSMS 22 · JOIN suggestions · Northwind sample schema'],styles:['assets/styles.png','SSMS 22 · Format styles with a live SQL preview'],history:['assets/history.png','SSMS 22 · SQL history and query versions'],fresh:['assets/sql-editor-fresh.png','Browser editor · captured 22 October 2026 · illustrative query, not executed'],postgres:['assets/postgresql-connect-fresh.png','PostgreSQL connection dialog · captured 22 October 2026 · demo values, no connection attempted']};
document.querySelectorAll('[data-shot]').forEach(button=>button.addEventListener('click',()=>{
 document.querySelectorAll('[data-shot]').forEach(b=>{b.classList.toggle('active',b===button);b.setAttribute('aria-pressed',String(b===button));});
 const [src,caption]=shots[button.dataset.shot];const img=document.getElementById('tour-shot');img.src=src;img.alt=caption;
 const zoom=img.closest('button');zoom.dataset.image=src;zoom.dataset.caption=caption;document.getElementById('tour-caption').textContent=caption;
}));
const dialog=document.getElementById('image-dialog');
document.querySelectorAll('[data-image]').forEach(button=>button.addEventListener('click',()=>{if(!dialog)return;dialog.querySelector('img').src=button.dataset.image;dialog.querySelector('img').alt=button.dataset.caption;dialog.querySelector('p').textContent=button.dataset.caption;dialog.showModal();}));
dialog?.querySelector('.close').addEventListener('click',()=>dialog.close());
dialog?.addEventListener('click',event=>{if(event.target===dialog)dialog.close();});
function showSelection(value){const node=document.getElementById('selection-message');if(node)node.textContent=`Selected: ${value.toUpperCase()}. Saved in this browser; tell me your choice in chat.`;document.querySelectorAll('[data-select]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.select===value)));}
document.querySelectorAll('[data-select]').forEach(button=>button.addEventListener('click',()=>{try{localStorage.setItem('akml-homepage-design',button.dataset.select);}catch{}showSelection(button.dataset.select);}));
try{const selected=localStorage.getItem('akml-homepage-design');if(['a','b','c'].includes(selected))showSelection(selected);}catch{}
