# Segurança e privacidade

## O que o programa acessa

O inventário é consultado pelo WMI do Windows após o clique em **Analisar meu computador**. As consultas estão em `desktop/Core.cs` e leem nomes de CPU/GPU, capacidade de RAM, fabricante/modelo da placa-mãe, versão/arquitetura do Windows e capacidade/espaço livre das unidades locais. Não consultam números de série, contas ou arquivos pessoais. O inventário permanece em memória até o encerramento e não é enviado à Steam.

A rotina de instância única usa o nome do próprio processo para localizar uma janela já aberta do MeuPcRoda. Não enumera arquivos pessoais nem transmite uma lista de processos.

## Conexões externas

- `store.steampowered.com`: recebe o termo de busca e o ID do jogo selecionado. O cliente é identificado como `MeuPcRoda/1.0`.
- KaBuM!, Amazon e Mercado Livre: links abertos no navegador padrão somente quando o usuário clica em pesquisar. A URL inclui o termo da peça pesquisada.
- As conexões externas também revelam ao serviço o IP de origem e os metadados normais de uma conexão HTTPS. O código não adiciona o inventário a essas requisições.

Não há telemetria, anúncios, atualização automática, porta HTTP local, serviço residente, inicialização automática ou pedido de administrador. O programa não executa PowerShell ou comandos do usuário. Os testes opcionais de diagnóstico gravam apenas os resultados dos testes no caminho informado, sem inventário.

## Como conferir uma distribuição

O pacote de código-fonte contém a licença, o código e os scripts de compilação. É possível compilar localmente sem baixar dependências de terceiros. O .NET Framework e as bibliotecas do Windows são dependências de plataforma e não fazem parte do código-fonte do MeuPcRoda.

Para conferir o arquivo recebido:

```powershell
Get-FileHash .\MeuPcRoda.exe -Algorithm SHA256
```

Compare com o hash publicado pela mesma origem confiável que disponibilizou a versão. Um hash identifica o arquivo; não comprova que ele é seguro, quem o criou ou que corresponde ao código-fonte. Uma cópia do hash recebida de origem comprometida não resolve esse problema. O script de compilação não promete binários idênticos entre máquinas; não há processo de build reproduzível ou atestação configurado.

Esta versão não tem assinatura digital de distribuição nem auditoria de segurança independente. A abertura do código facilita a revisão, mas não é garantia de ausência de vulnerabilidades. Não é necessário desativar o antivírus, instalar certificados ou reduzir proteções para compilar o projeto.

## Relatar um problema

Inclua versão, passos para reproduzir, comportamento esperado e observado. Remova nomes de usuário, caminhos privados, tokens e dados pessoais dos anexos. Para defeitos comuns, use as issues do repositório oficial: https://github.com/FilipeLacerda738/MeuPcRoda/issues. Ainda não há um canal privado de segurança configurado; não publique dados sensíveis ou detalhes de exploração em discussões públicas.
