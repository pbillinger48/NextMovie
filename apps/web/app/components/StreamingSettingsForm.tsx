"use client";

import Image from "next/image";
import { useActionState, useRef, useState } from "react";

import { saveStreamingSettings } from "@/app/actions/streaming";
import type { CountryOption, StreamingServiceOption } from "@nextmovie/api-client";

const LOGO_BASE_URL = "https://image.tmdb.org/t/p/w92";

type StreamingSettingsFormProps = {
  region: string;
  countries: CountryOption[];
  services: StreamingServiceOption[];
};

/**
 * Region and subscriptions, saved together.
 *
 * Changing the country submits immediately rather than waiting for the save
 * button. The list of services below depends on the country, so leaving the two
 * out of step would show somebody British services while their account still
 * said they were in the United States.
 */
export function StreamingSettingsForm({
  region,
  countries,
  services,
}: StreamingSettingsFormProps) {
  const [state, submit, pending] = useActionState(saveStreamingSettings, undefined);
  const [filter, setFilter] = useState("");
  const form = useRef<HTMLFormElement>(null);

  const offered = services.filter((service) => service.offeredHere);

  // Services they subscribe to that this country does not offer. Shown rather
  // than dropped: the subscription is still stored and still shaping
  // recommendations, so hiding it would make it impossible to undo.
  const elsewhere = services.filter((service) => !service.offeredHere);

  const needle = filter.trim().toLowerCase();
  const visible =
    needle === "" ? offered : offered.filter((service) => service.name.toLowerCase().includes(needle));

  const countryName = countries.find((country) => country.code === region)?.name ?? region;

  return (
    <form ref={form} action={submit} className="flex w-full max-w-2xl flex-col gap-6">
      {state?.message ? (
        <p
          role="alert"
          className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        >
          {state.message}
        </p>
      ) : null}

      {state?.saved ? (
        <p
          aria-live="polite"
          className="rounded-md border border-green-300 bg-green-50 px-4 py-3 text-sm text-green-900 dark:border-green-900 dark:bg-green-950 dark:text-green-100"
        >
          Streaming settings saved.
        </p>
      ) : null}

      <div className="flex flex-col gap-1">
        <label htmlFor="region" className="text-sm font-medium">
          Where you watch
        </label>
        <select
          id="region"
          name="region"
          defaultValue={region}
          onChange={() => form.current?.requestSubmit()}
          aria-describedby={state?.fieldErrors?.Region ? "region-error" : "region-hint"}
          aria-invalid={state?.fieldErrors?.Region ? true : undefined}
          className="max-w-xs rounded-md border border-neutral-300 bg-white px-3 py-2 text-base shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900"
        >
          {countries.map((country) => (
            <option key={country.code} value={country.code}>
              {country.name}
            </option>
          ))}
        </select>
        {state?.fieldErrors?.Region ? (
          <p id="region-error" className="text-sm text-red-700 dark:text-red-300">
            {state.fieldErrors.Region.join(" ")}
          </p>
        ) : (
          <p id="region-hint" className="text-sm text-neutral-600 dark:text-neutral-400">
            Streaming catalogues differ by country. Changing this saves straight away.
          </p>
        )}
      </div>

      <fieldset className="flex flex-col gap-3 border-t border-neutral-200 pt-6 dark:border-neutral-800">
        <legend className="sr-only">Services you subscribe to</legend>

        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-medium">What you subscribe to</h2>
          <label htmlFor="service-filter" className="sr-only">
            Filter services
          </label>
          <input
            id="service-filter"
            type="search"
            value={filter}
            onChange={(event) => setFilter(event.target.value)}
            placeholder="Filter services…"
            className="w-48 rounded-md border border-neutral-300 bg-white px-3 py-1.5 text-sm shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900"
          />
        </div>

        {offered.length === 0 ? (
          <p className="text-sm text-neutral-600 dark:text-neutral-400">
            We don’t know which services operate in {countryName} yet. Recommendations will
            still work — they just won’t say where to watch anything.
          </p>
        ) : null}

        {/* Scrolls rather than running to a hundred rows down the page. The list
            is complete on purpose, so it needs somewhere to go. */}
        <ul className="max-h-96 divide-y divide-neutral-200 overflow-y-auto rounded-md border border-neutral-200 dark:divide-neutral-800 dark:border-neutral-800">
          {visible.map((service) => (
            <ServiceRow key={service.id} service={service} />
          ))}
        </ul>

        {visible.length === 0 && offered.length > 0 ? (
          <p aria-live="polite" className="text-sm text-neutral-600 dark:text-neutral-400">
            No services match “{filter}”.
          </p>
        ) : null}

        {elsewhere.length > 0 ? (
          <div className="flex flex-col gap-2">
            <h3 className="text-sm font-medium">Not offered in {countryName}</h3>
            <p className="text-sm text-neutral-600 dark:text-neutral-400">
              You told us about these before. They still count, so untick any you no longer
              have.
            </p>
            <ul className="divide-y divide-neutral-200 rounded-md border border-neutral-200 dark:divide-neutral-800 dark:border-neutral-800">
              {elsewhere.map((service) => (
                <ServiceRow key={service.id} service={service} />
              ))}
            </ul>
          </div>
        ) : null}
      </fieldset>

      <button
        type="submit"
        disabled={pending}
        className="self-start rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 disabled:opacity-60"
      >
        {pending ? "Saving…" : "Save services"}
      </button>
    </form>
  );
}

function ServiceRow({ service }: { service: StreamingServiceOption }) {
  return (
    <li>
      <label className="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm hover:bg-neutral-50 dark:hover:bg-neutral-900">
        <input
          type="checkbox"
          name="providerIds"
          value={service.id}
          defaultChecked={service.subscribed}
          className="size-4 rounded border-neutral-400 text-blue-600 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
        />
        {service.logoPath ? (
          <Image
            src={`${LOGO_BASE_URL}${service.logoPath}`}
            alt=""
            width={24}
            height={24}
            className="size-6 rounded"
          />
        ) : (
          <span aria-hidden className="size-6 rounded bg-neutral-200 dark:bg-neutral-800" />
        )}
        <span>{service.name}</span>
      </label>
    </li>
  );
}
