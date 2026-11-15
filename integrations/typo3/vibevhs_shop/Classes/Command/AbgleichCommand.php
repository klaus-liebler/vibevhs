<?php

declare(strict_types=1);

namespace Vhs\VibevhsShop\Command;

use Symfony\Component\Console\Attribute\AsCommand;
use Symfony\Component\Console\Command\Command;
use Symfony\Component\Console\Input\InputInterface;
use Symfony\Component\Console\Input\InputOption;
use Symfony\Component\Console\Output\OutputInterface;
use Vhs\VibevhsShop\Service\Abgleich;

/**
 * Delta-Abgleich mit VibeVHS. Als Scheduler-Task einrichten: Scheduler → „Execute console commands“ → vibevhs:abgleich
 * (z. B. alle 15 Minuten). Mit --voll wird der komplette Bestand neu geholt (z. B. nächtlich).
 */
#[AsCommand(name: 'vibevhs:abgleich', description: 'Kursdaten aus VibeVHS abgleichen (Delta seit dem letzten Stand)')]
final class AbgleichCommand extends Command
{
    public function __construct(private readonly Abgleich $abgleich)
    {
        parent::__construct();
    }

    protected function configure(): void
    {
        $this->addOption('voll', null, InputOption::VALUE_NONE, 'Alle Kurse holen statt nur Änderungen');
    }

    protected function execute(InputInterface $input, OutputInterface $output): int
    {
        try {
            $e = $this->abgleich->ausfuehren(null, (bool)$input->getOption('voll'));
        } catch (\Throwable $f) {
            $output->writeln('<error>Abgleich fehlgeschlagen: ' . $f->getMessage() . '</error>');
            return Command::FAILURE;
        }

        $output->writeln(sprintf('%d Kurse aktualisiert, %d entfernt, Stand %s', $e['geaendert'], $e['entfernt'], $e['stand']));
        return Command::SUCCESS;
    }
}
