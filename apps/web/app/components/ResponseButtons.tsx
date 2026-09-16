import { respond, withdraw } from "@/app/actions/responses";

const BUTTON =
  "rounded-md border px-3 py-1.5 text-xs font-medium transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600";

const IDLE = `${BUTTON} border-neutral-300 hover:bg-neutral-50 dark:border-neutral-700 dark:hover:bg-neutral-800`;

type ResponseButtonsProps = {
  movieId: string;
  /** `Saved`, `NotInterested`, or null when there is no standing answer. */
  response: string | null;
  watched: boolean;
};

/**
 * Save it, hide it, or say you have seen it.
 *
 * Plain forms posting to Server Actions, like the rating control — every button
 * works before any client JavaScript loads, and each one says what it does rather
 * than relying on an icon.
 *
 * The three are not exclusive states of one control: "seen it" is recorded as a
 * viewing rather than a response (ADR-0011), so a watched film has no standing
 * answer to show as pressed. Rendering them as a radio group would imply an
 * exclusivity the data does not have.
 */
export function ResponseButtons({ movieId, response, watched }: ResponseButtonsProps) {
  if (watched) {
    return (
      <p className="text-xs text-neutral-600 dark:text-neutral-400">
        You&apos;ve seen this. It won&apos;t be recommended again.
      </p>
    );
  }

  if (response === "Saved") {
    return (
      <Row>
        <span className="text-xs font-medium text-green-700 dark:text-green-400">
          On your watchlist
        </span>
        <Action movieId={movieId} action={withdraw} label="Remove" />
        <Action movieId={movieId} response="Seen" label="Seen it" />
      </Row>
    );
  }

  if (response === "NotInterested") {
    return (
      <Row>
        <span className="text-xs text-neutral-600 dark:text-neutral-400">Hidden</span>
        <Action movieId={movieId} action={withdraw} label="Undo" />
      </Row>
    );
  }

  return (
    <Row>
      <Action movieId={movieId} response="Saved" label="Save" />
      <Action movieId={movieId} response="NotInterested" label="Not interested" />
      <Action movieId={movieId} response="Seen" label="Seen it" />
    </Row>
  );
}

function Row({ children }: { children: React.ReactNode }) {
  return <div className="flex flex-wrap items-center gap-2">{children}</div>;
}

function Action({
  movieId,
  response,
  action = respond,
  label,
}: {
  movieId: string;
  response?: string;
  action?: (formData: FormData) => Promise<void>;
  label: string;
}) {
  return (
    <form action={action}>
      <input type="hidden" name="movieId" value={movieId} />
      {response ? <input type="hidden" name="response" value={response} /> : null}
      <button type="submit" className={IDLE}>
        {label}
      </button>
    </form>
  );
}
