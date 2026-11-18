# MeuPcRoda

Aplicativo leve e portátil para Windows que identifica os componentes do computador, pesquisa jogos na Steam e compara os requisitos publicados pela desenvolvedora.

**[Baixar o MeuPcRoda 1.0 para Windows](https://github.com/FilipeLacerda738/MeuPcRoda/releases/tag/v1.0.0)** · [Todas as versões](https://github.com/FilipeLacerda738/MeuPcRoda/releases)

## Como usar

1. Baixe `MeuPcRoda-Windows.zip` na release e extraia os arquivos.
2. Abra `MeuPcRoda.exe`.
3. Clique em **Analisar meu computador**.
4. Busque um jogo, selecione o resultado e escolha requisitos mínimos ou recomendados.
5. Confira cada componente e as orientações apresentadas.

Requer Windows 10/11 com **.NET Framework 4.8 ou 4.8.1**. O runtime é compartilhado com o Windows e não está incluído no executável. Se necessário, consulte a [instalação oficial da Microsoft](https://learn.microsoft.com/dotnet/framework/install/guide-for-developers). A consulta de jogos precisa de internet; a leitura do hardware é local.

O aplicativo não precisa de instalação própria, Node.js, servidor local ou navegador embutido. Os links das lojas abrem no navegador padrão. Nenhum arquivo JSON é necessário para executar o programa.

## Funcionalidades e limites

- Identificação de processador, adaptadores de vídeo, memória RAM, placa-mãe, Windows e espaço livre por unidade.
- Busca de jogos para Windows e consulta de requisitos na Steam, com origem e data da consulta.
- Comparação numérica de RAM e espaço. O espaço deve caber em uma unidade; discos separados não são somados.
- CPU/GPU aprovadas somente quando o modelo corresponde a uma alternativa explícita do requisito. Modelos diferentes ficam pendentes; não há base de equivalências de desempenho nesta versão.
- Pesquisas em KaBuM!, Amazon e Mercado Livre a partir de requisitos específicos. São links de pesquisa, sem preços, vendedores ou compatibilidade de compra validados.

A análise não confirma que o jogo rodará enquanto houver itens pendentes. Windows, DirectX, recursos gráficos, fonte, conectores e dimensões do gabinete precisam de revisão. Não há estimativas de FPS. Os endpoints públicos da Steam podem mudar ou ficar indisponíveis; alguns jogos não informam requisitos completos.

## Privacidade

O inventário é lido após o clique e fica em memória durante a sessão. Não é enviado à Steam nem gravado em disco. O programa não acessa arquivos pessoais ou números de série, não instala serviços e não inicia com o Windows. Consulte [SECURITY.md](SECURITY.md) para os acessos, conexões externas e limitações de segurança.

## Compilar e testar

Use PowerShell no Windows com .NET Framework 4.8/4.8.1:

```powershell
# Compilar usando o compilador do .NET Framework do Windows
.\desktop\build.ps1

# Compilar, executar os testes locais e gerar os pacotes
.\desktop\package.ps1
```

Os scripts não baixam dependências. O resultado fica em `release/`, que não é versionada. A interface XAML e o ícone estão embutidos no EXE. Para testes de integração com WMI e Steam:

```powershell
$report = Join-Path $env:TEMP 'MeuPcRoda-integracao.json'
$process = Start-Process .\release\MeuPcRoda.exe -ArgumentList '--integration-test', ('"' + $report + '"') -Wait -PassThru
Get-Content -LiteralPath $report
```

O relatório é apenas um diagnóstico opcional e pode ser apagado depois. O empacotamento usa um relatório temporário, removido automaticamente. Nenhum relatório JSON é incluído nos pacotes.

## Estrutura e contribuições

- `desktop/App.cs`: inicialização, eventos e apresentação.
- `desktop/Core.cs`: WMI, cliente Steam e comparação.
- `desktop/MainWindow.xaml`: interface e estilos.
- `desktop/Diagnostics.cs`: testes locais e de integração.
- `desktop/build.ps1` e `desktop/package.ps1`: compilação e distribuição.
- `LICENSE` e `SECURITY.md`: licença e segurança.

Relate defeitos ou proponha melhorias nas [issues](https://github.com/FilipeLacerda738/MeuPcRoda/issues). Contribuições devem preservar o baixo consumo, evitar coleta de dados pessoais e manter análises pendentes quando faltarem evidências. Mudanças visuais precisam ser conferidas em diferentes tamanhos de janela e escalas de tela.

## Licença e distribuição

[MIT](LICENSE): permite usar, copiar, modificar, redistribuir e vender o código ou aplicativos derivados, mantendo o aviso de copyright e a licença. Componentes do Windows seguem suas próprias licenças.

Os pacotes incluem a licença e um arquivo `SHA256.txt` é disponibilizado na release. Hashes conferem integridade quando comparados com uma origem confiável; não são garantia de segurança ou correspondência com o código. Ainda não há auditoria independente, assinatura digital de distribuição ou build reproduzível verificado.
