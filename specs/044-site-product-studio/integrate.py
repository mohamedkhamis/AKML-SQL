"""One-time import of approved concept A content into the existing Razor homepage."""
from pathlib import Path
import re
import shutil

repo = Path(__file__).resolve().parents[2]
preview = repo / 'reports/marketing-homepage-2026-10-22'
site = repo / 'src/AkmlSql.Site'
html = (preview / 'a.html').read_text(encoding='utf-8')
body = html.split('<main id="main">', 1)[1].split('</main>', 1)[0]
body = body.replace('https://akml.khamis.work', '')
body = body.replace('id="workflow"', 'id="product-tour"').replace('href="#workflow"', 'href="#product-tour"')
body = body.replace('href="/docs">Read the setup guide', 'href="/docs/topics/browser-setup">Read the setup guide')
body = body.replace('href="/docs">Read the AI setup', 'href="/docs/topics/ai-assistance">Read the AI setup')
body = body.replace('assets/sql-server.png', 'img/databases/sql-server.png').replace('assets/postgresql.png', 'img/databases/postgresql.png')
body = body.replace('class="button"', 'class="btn btn-primary"').replace('class="button secondary"', 'class="btn btn-secondary"')
body = body.replace('class="hero-a wrap"', 'class="hero-a wrap hero"')
body = body.replace('class="lead"', 'class="lead hero-tagline"', 1)
body = body.replace('<p class="micro">', '<p class="hero-tagline-sub">Free and open source under the MIT license.</p><p class="micro">', 1)
body = body.replace('Download AKML <span>↗</span></a>', '''Download AKML <span aria-hidden="true">↗</span>
@if (Manifest.Latest is { } latest)
{
    <span class="btn-sub">Windows · @latest.Version</span>
}
</a>''', 1)
body = re.sub(r'<figure class="product-shot">.*?</figure>', '''<ProductScreenshot Name="browser-workspace" DarkPath="img/screenshots/query-results.png" LightPath="img/screenshots/query-results.png"
    Width="1920" Height="889" Priority="true" Class="product-shot"
    Alt="AKML browser workspace showing a SQL Server query and Northwind sample results."
    Caption="Browser workspace · SQL Server · Northwind sample data" />''', body, flags=re.S)
items = [
('completion', '01', 'Write with context', 'Find related tables and JOIN conditions.', '05-join-suggestions', 740, 476, 'AKML JOIN suggestions in SSMS 22 using the Northwind sample schema.', 'SSMS 22 · JOIN suggestions · Northwind sample schema', None),
('styles', '02', 'Keep a consistent style', 'Adjust formatting with a live SQL preview.', '02-format-styles', 1046, 673, 'AKML Format styles options beside a live SQL preview in SSMS 22.', 'SSMS 22 · Format styles and live preview', None),
('history', '03', 'Pick up where you left off', 'Search previous queries and inspect versions.', '01-sql-history', 804, 558, 'AKML SQL History showing grouped queries and a preview in SSMS 22.', 'SSMS 22 · SQL history and query versions', None),
('editor', '04', 'Meet the browser editor', 'An illustrative SQL query with analysis findings.', 'browser-editor', 1440, 900, 'Actual AKML browser editor with an illustrative Northwind query and analysis findings; the query was not executed.', 'Browser editor · illustrative query, not executed · captured 22 October 2026', 'sql-editor-fresh.png'),
('postgres', '05', 'Set up PostgreSQL', 'A real connection screen with demo values.', 'postgresql-connect', 720, 600, 'Actual AKML PostgreSQL connection dialog with synthetic demo values and no password.', 'PostgreSQL connection dialog · demo values · captured 22 October 2026', 'postgresql-connect-fresh.png'),
]
controls=[];panels=[]
for key,num,title,desc,name,w,h,alt,caption,asset in items:
    controls.append(f'<button type="button" class="studio-tour-button" aria-pressed="false" aria-controls="tour-{key}"><span>{num}</span><strong>{title}</strong><small>{desc}</small></button>')
    paths = f' DarkPath="img/screenshots/marketing/{asset}" LightPath="img/screenshots/marketing/{asset}"' if asset else ''
    panels.append(f'''<div class="studio-tour-panel media-row" id="tour-{key}"><div class="media-text"><h3>{title}</h3><p>{desc}</p></div>
    <ProductScreenshot Name="{name}"{paths} Width="{w}" Height="{h}" Alt="{alt}" Caption="{caption}" Class="tour-image" /></div>''')
tour='<div class="studio-tour"><div class="tour-controls" role="group" aria-label="Product screenshot selection" hidden>'+''.join(controls)+'</div><div class="tour-panels">'+''.join(panels)+'</div></div>'
body=re.sub(r'<div class="tour">.*?</figure></div>', lambda _:tour, body, flags=re.S)
# The final artwork sentence was a review note, not visitor-facing product information.
body=body.replace(' The SQL Server artwork identifies the database family, not a tested version list.', '')
head='''@page "/"
@using AkmlSql.Site.Releases
@inject ReleasesManifest Manifest

<PageTitle>AKML SQL — SQL Server and PostgreSQL productivity</PageTitle>
<HeadContent>
    <meta name="description" content="Write, review and reuse SQL with AKML. Completion, formatting, analysis and history for SQL Server in SSMS 22 or your browser, and PostgreSQL in the browser. Free and open source." />
    <meta property="og:title" content="AKML SQL — Good SQL. Less friction." />
    <meta property="og:description" content="A practical SQL toolkit for SQL Server and PostgreSQL. Explore real product screens and choose your workspace." />
</HeadContent>

@* Spec 044: approved Product Studio direction. All examples render on the server;
   product-tour.js progressively adds selection, while the existing viewer owns zoom. *@
<div class="studio-home">
'''
body=body.replace('><', '>\n<')
(site/'Components/Pages/Home.razor').write_text(head+body+'\n</div>\n', encoding='utf-8')
for folder,names in [('databases',['sql-server.png','postgresql.png']),('screenshots/marketing',['sql-editor-fresh.png','postgresql-connect-fresh.png'])]:
    target=site/'wwwroot/img'/folder
    target.mkdir(parents=True,exist_ok=True)
    for name in names:shutil.copy2(preview/'assets'/name,target/name)
print('Integrated concept A markup and unchanged source images.')
