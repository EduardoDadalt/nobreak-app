# Nobreak Monitor

Serviço do Windows + painel desktop para nobreaks TS Shara (protocolo Megatec via USB-serial).
Quando o nobreak entra na bateria, o serviço desliga o computador depois de X segundos. Se a
energia voltar antes disso, ele cancela o desligamento.

## Estrutura

| Projeto | O que é |
|---|---|
| `src/NobreakMonitor.Core` | Protocolo Megatec (Q1/F/I), estimativa de carga, política de desligamento, contrato de IPC |
| `src/NobreakMonitor.Service` | Serviço do Windows (LocalSystem, início automático): serial, histórico, desligamento, named pipe |
| `src/NobreakMonitor.App` | Painel WPF com ícone na bandeja: status, testes, configurações, histórico |
| `tests/NobreakMonitor.Core.Tests` | Testes do protocolo e da política de desligamento |

```
Nobreak ──USB/COM (2400 8N1)──► NobreakMonitor.Service ──\\.\pipe\NobreakMonitor──► NobreakMonitor.App
                                  │  config.json / events.jsonl em %ProgramData%\NobreakMonitor
                                  └► shutdown.exe /s (motivo: falha de energia)
```

O monitoramento e o desligamento rodam no serviço, então funcionam com o painel fechado e até sem
ninguém logado. O painel só exibe as informações e altera a configuração.

## Instalação

Pré-requisito: .NET 10 SDK. Feche o **TSApp Desktop**, porque ele ocupa a porta COM do nobreak.

```powershell
# PowerShell como Administrador, na raiz do repositório
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1
```

O script:
- publica tudo em `C:\Program Files\NobreakMonitor`;
- registra o serviço `NobreakMonitor` com **inicialização Automática** (sobe junto com o Windows, antes do login);
- configura a recuperação para reiniciar o serviço após 5 s, 5 s e 30 s em caso de falha;
- cria o atalho "Nobreak Monitor" no Menu Iniciar;
- se ainda não houver configuração, cria uma com o **modo simulação ligado**.

Para remover: `.\scripts\uninstall-service.ps1`. Acrescente `-RemoveData` para apagar também a configuração e o histórico.

## Configuração (aba "Configurações")

| Opção | Padrão | Efeito |
|---|---|---|
| Desligar na bateria após N s | ligado, 120 s | Inicia a contagem quando o bit "falha de rede" do Q1 fica 1 |
| Desligar em bateria baixa após N s | ligado, 10 s | Inicia ou encurta a contagem |
| Fechar programas à força | ligado | `shutdown /f` |
| Desligar a saída do nobreak após N min | 0 (não) | Envia `S<n>` ao nobreak logo antes de desligar o PC |
| Modo simulação | ligado na 1ª instalação | Registra "o computador seria desligado" no histórico, sem desligar |
| Porta COM | automático | Testa as portas com `I`/`Q1`. Você pode fixar uma, por exemplo `COM3` |

Regras: leituras durante um teste de bateria são ignoradas. Se a comunicação cair, a contagem em
andamento continua. Um cancelamento pelo painel vale até a energia voltar, mas bateria baixa ainda
reativa a contagem.

## Como testar o desligamento (quando puder)

1. Com o **modo simulação ligado**, use um atraso curto (ex.: 20 s) e tire o nobreak da tomada.
   O painel mostra a contagem. Recoloque na tomada e confirme o cancelamento. Repita sem recolocar
   e confira no Histórico a linha "[SIMULAÇÃO] O computador seria desligado agora".
2. Salve o seu trabalho, **desligue o modo simulação**, clique em Salvar e repita o passo 1.

## Engenharia reversa do TSApp Desktop 4.4.6

Estas informações foram obtidas lendo o `resources/app.asar` (Electron, código JS não ofuscado):
- Serial a **2400 bps**. A detecção envia `I\r\n` em cada porta e aceita a que responder contendo `TS`.
- A cada 2 s envia `Q1`. A resposta tem 8 campos, e o último traz os bits de status
  b7..b0 = falha de rede, bateria baixa, bypass, falha do UPS, standby, teste, desligamento ativo e beep.
- Ao conectar, envia `F`. Comandos do painel: `T` (10 s), `TL` (até bateria baixa), `T<nn>` (minutos),
  `Q` (beep) e `S<nn>` (desliga a saída do nobreak em nn minutos).
- A carga da bateria é estimada por interpolação de tensão (tabela de 12,20 V a 10,50 V, ou x2 para 24 V).
  Essa lógica foi portada em `BatteryCharge`.
- As opções "desligar sem rede / bateria baixa após X min" do TSApp **enviam `S<x>` ao nobreak**, ou seja,
  desligam a saída do nobreak, e **não fazem o desligamento ordenado do Windows**. Existe um handler
  `system:shutdown` (`shutdown /s /t 0`) no processo principal, mas ele não é exposto pelo preload,
  e nenhuma chamada a ele foi encontrada no código do renderer.

Respostas reais do nobreak testado (COM3):
```
I  → #TS SHARA 221011 Senoid  18 V01020100F
F  → #Univ. 100 24.00 60.0          (tensão nominal "Univ." = bivolt)
Q1 → (221.0 221.0 112.0 022 60.0 27.2 29.0 00001001
```
